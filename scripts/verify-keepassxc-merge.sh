#!/usr/bin/env bash
#
# verify-keepassxc-merge.sh
#
# PERMANENT COMPATIBILITY GATE — docs/PRODUCT.md law 4.6, the merge half (1.4b3).
#
# The app holds a vault KeePassXC made, through tests/Keypaste.AppDriver hold as launch composes it,
# while KeePassXC saves the same file with keepassxc-cli, edits it, merges into it, or has a copy it
# exported, edited and imported moved over it as a sync service moves one. keypaste merges each save
# with the copy it holds (D-0537 to D-0543), and KeePassXC then exports the result and its header is
# checked after every merge (G12):
#
#   G1   KeePassXC's save to one item refuses the app's edit of another; keypaste saves once, both
#        changes are in KeePassXC's export, and the notice names the edit as saved.
#   G2   KeePassXC saves and then the app edits; and the app saves before a KeePassXC copy taken earlier
#        is moved over the file, its saved change surviving and KeePassXC's current where newer.
#   G3   KeePassXC and the app change one item in both orders of time, the newer current and the older
#        in history; KeePassXC's edit of the app's saved version measures whether its round trip keeps
#        a version intact, which lineage needs (1.4a).
#   G4   The open copy, written to a file, merged into a copy of the file by `keepassxc-cli merge -s`,
#        against keypaste's result: the same where both agree, and each difference D-0537 to D-0541 make
#        on purpose asserted.
#   G5   A purge of an item the app did not change goes with its tombstone; one it changed is in the bin.
#   G6   An item KeePassXC recycles while the app edits it ends in the bin with the edit, and one the app
#        restores after KeePassXC purged it survives.
#   G7   Attachments keep their bytes, current and in history.
#   G8   A revoked token stays revoked after KeePassXC merges in a copy still holding it (1.4b5).
#   G11  A changed password locks the app; another vault is not merged and the notice says so; a
#        truncated file is refused and the complete one then merged.
#
# Usage:  scripts/verify-keepassxc-merge.sh <work-directory>
# Env:    KP_COMPAT_PASSWORD   master password for the vaults        (required)
#         KPXC_CLI             path to keepassxc-cli                 (default: PATH lookup)
#         KEYPASTE_BIN         path to the keypaste binary           (default: the Release build)
#         KEYPASTE_APP_DRIVER  path to tests/Keypaste.AppDriver      (default: the Release build)
#
# NEGATIVE CONTROL: it fails if a change either program saved is missing from KeePassXC's export, a
# deletion keypaste should apply comes back, an item kept against a deletion is not where the rules
# put it, an attachment's bytes change, a revoked token runs, a refused merge is merged or not said,
# KeePassXC cannot open a result, or keypaste and KeePassXC agree where the rules say they differ.
set -euo pipefail

# shellcheck source=scripts/lib/common.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"
DIE_PREFIX='MERGE GATE FAILED: '
watchdog "${WATCHDOG_SECONDS:-1200}" keypaste Keypaste.AppDriver keepassxc-cli

dir=${1:-}
[ -n "$dir" ] || die "usage: verify-keepassxc-merge.sh <work-directory>"
# shellcheck source=scripts/lib/kpxc.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib/kpxc.sh"
require jq
require node
kp=$(keypaste_bin)
drv=$(app_driver)

rm -rf "$dir"
mkdir -p "$dir/home"
KEYPASTE_HOME=$(native "$dir/home")
export KEYPASTE_HOME
unset KEYPASTE_VAULT KEYPASTE_KEYFILE KEYPASTE_APPROVER
audit="$dir/home/audit.jsonl"

hold_out=
hold_pid=
holds=0
DIE_FILES=hold_out
# Fixed descriptors, as macOS's bash 3.2 needs (D-0398): 7 is the held app's standard input.
exec 7>/dev/null
cleanup() {
  exec 7>&- 2>/dev/null || true
  if [ -n "$hold_pid" ]; then kill_process "$hold_pid"; fi
}
trap cleanup EXIT

# Reads KeePassXC's XML exports, and edits one into the copy KeePassXC imports: node merge-xml.js <command> <file> [...].
cat >"$dir/merge-xml.js" <<'JS'
"use strict";
const fs = require("node:fs");
const zero = "AAAAAAAAAAAAAAAAAAAAAA==";

function decode(s) {
  return s.replace(/&lt;/g, "<").replace(/&gt;/g, ">").replace(/&quot;/g, "\"").replace(/&apos;/g, "'").replace(/&amp;/g, "&");
}

function encode(s) {
  return s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
}

function parse(text) {
  const root = { name: "#document", attrs: "", children: [] };
  const stack = [root];
  const pattern = /<\?[^>]*\?>|<!--[\s\S]*?-->|<(\/?)([A-Za-z_][\w.-]*)([^>]*?)(\/?)>|([^<]+)/g;
  let match;
  while ((match = pattern.exec(text))) {
    const [, close, name, attrs, selfClose, chars] = match;
    const top = stack[stack.length - 1];
    if (chars !== undefined) {
      if (chars.trim()) top.children.push({ text: decode(chars) });
    } else if (name && close) {
      stack.pop();
    } else if (name) {
      const node = { name, attrs, children: [] };
      top.children.push(node);
      if (!selfClose) stack.push(node);
    }
  }
  return root;
}

function serialize(node) {
  if (node.text !== undefined) return encode(node.text);
  return `<${node.name}${node.attrs}>${node.children.map(serialize).join("")}</${node.name}>`;
}

const el = (name, value) => ({ name, attrs: "", children: value === undefined ? [] : [{ text: String(value) }] });
const kids = (node, name) => (node ? node.children.filter((c) => c.name === name) : []);
const kid = (node, name) => kids(node, name)[0];
const text = (node) => (node ? node.children.filter((c) => c.text !== undefined).map((c) => c.text).join("") : "");
const attr = (node, name) => (node && new RegExp(`${name}="([^"]*)"`).exec(node.attrs) || [])[1];

function child(node, name) {
  let found = kid(node, name);
  if (!found) {
    found = el(name);
    node.children.push(found);
  }
  return found;
}

function set(node, name, value) {
  child(node, name).children = [{ text: String(value) }];
}

// A KDBX 4 export writes a time as the base64 of its little-endian count of seconds since 0001-01-01.
function seconds(value) {
  if (!value) return null;
  if (/^\d{4}-/.test(value)) return Date.parse(value) / 1000;
  return Number(Buffer.from(value, "base64").readBigInt64LE()) - 62135596800;
}

const iso = (s) => new Date(s * 1000).toISOString().replace(/\.\d{3}Z$/, "Z");

function file(name) {
  const doc = parse(fs.readFileSync(name, "utf8"));
  const keepass = kid(doc, "KeePassFile");
  return { doc, meta: kid(keepass, "Meta"), root: kid(keepass, "Root") };
}

// Every group, with its parent and path, and every current entry, with its group.
function walk(db) {
  const groups = new Map();
  const entries = new Map();
  const visit = (group, parent, path) => {
    const id = text(kid(group, "UUID"));
    groups.set(id, { node: group, parent, path });
    for (const entry of kids(group, "Entry")) entries.set(text(kid(entry, "UUID")), { node: entry, group: id, groupNode: group, path });
    for (const sub of kids(group, "Group")) visit(sub, id, path ? `${path}/${text(kid(sub, "Name"))}` : text(kid(sub, "Name")));
  };
  visit(kid(db.root, "Group"), null, "");
  return { groups, entries };
}

function version(entry) {
  const strings = {};
  for (const s of kids(entry, "String")) strings[text(kid(s, "Key"))] = text(kid(s, "Value"));
  const times = kid(entry, "Times");
  const data = {};
  for (const item of kids(kid(entry, "CustomData"), "Item")) data[text(kid(item, "Key"))] = text(kid(item, "Value"));
  return {
    title: strings.Title || "",
    username: strings.UserName || "",
    password: strings.Password || "",
    modified: seconds(text(kid(times, "LastModificationTime"))),
    location: seconds(text(kid(times, "LocationChanged"))),
    previous: text(kid(entry, "PreviousParentGroup")) || null,
    attachments: kids(entry, "Binary").map((b) => ({ name: text(kid(b, "Key")), ref: attr(kid(b, "Value"), "Ref") || null })),
    data,
  };
}

function markers41(db, walked) {
  for (const { node } of walked.groups.values()) {
    if (text(kid(node, "Tags")).trim()) return true;
    if ((text(kid(node, "PreviousParentGroup")) || zero) !== zero) return true;
  }
  for (const { node } of walked.entries.values()) {
    if (/^false$/i.test(text(kid(node, "QualityCheck")))) return true;
    if ((text(kid(node, "PreviousParentGroup")) || zero) !== zero) return true;
  }
  for (const icon of kids(kid(db.meta, "CustomIcons"), "Icon")) {
    if (text(kid(icon, "Name")) || text(kid(icon, "LastModificationTime"))) return true;
  }
  return kids(kid(db.meta, "CustomData"), "Item").some((item) => text(kid(item, "LastModificationTime")));
}

function craft(db, spec) {
  const walked = walk(db);
  const entry = (id) => walked.entries.get(id) || (() => { throw new Error(`no entry ${id}`); })();
  const group = (id) => walked.groups.get(id) || (() => { throw new Error(`no group ${id}`); })();
  const deleted = child(db.root, "DeletedObjects");
  const remove = (found) => { found.groupNode.children = found.groupNode.children.filter((c) => c !== found.node); };
  for (const op of spec) {
    if (op.op === "edit") {
      const found = entry(op.entry);
      if (op.keep) {
        const copy = JSON.parse(JSON.stringify(found.node));
        copy.children = copy.children.filter((c) => c.name !== "History");
        child(found.node, "History").children.push(copy);
      }
      for (const [key, value] of Object.entries(op.strings || {})) {
        const string = kids(found.node, "String").find((s) => text(kid(s, "Key")) === key);
        if (string) kid(string, "Value").children = [{ text: value }];
        else found.node.children.push({ name: "String", attrs: "", children: [el("Key", key), el("Value", value)] });
      }
      if (op.modified) set(child(found.node, "Times"), "LastModificationTime", op.modified);
      if (op.location) set(child(found.node, "Times"), "LocationChanged", op.location);
    } else if (op.op === "history") {
      const found = entry(op.entry);
      const history = kids(kid(found.node, "History"), "Entry");
      const copy = JSON.parse(JSON.stringify(history[op.like]));
      for (const [key, value] of Object.entries(op.strings)) {
        kid(kids(copy, "String").find((s) => text(kid(s, "Key")) === key), "Value").children = [{ text: value }];
      }
      const versions = kid(found.node, "History").children;
      if (op.replace) versions[versions.indexOf(history[op.like])] = copy;
      else versions.push(copy);
    } else if (op.op === "purge") {
      remove(entry(op.entry));
      deleted.children.push({ name: "DeletedObject", attrs: "", children: [el("UUID", op.entry), el("DeletionTime", op.at)] });
    } else if (op.op === "move") {
      const found = entry(op.entry);
      remove(found);
      group(op.to).node.children.push(found.node);
      set(child(found.node, "Times"), "LocationChanged", op.location);
      if (op.previous) set(found.node, "PreviousParentGroup", op.previous);
    } else if (op.op === "group") {
      const found = group(op.group);
      if (op.tags !== undefined) set(found.node, "Tags", op.tags);
      if (op.searching !== undefined) set(found.node, "EnableSearching", op.searching);
      for (const [key, value] of Object.entries(op.data || {})) {
        child(found.node, "CustomData").children.push({ name: "Item", attrs: "", children: [el("Key", key), el("Value", value)] });
      }
      if (op.modified) set(child(found.node, "Times"), "LastModificationTime", op.modified);
    } else if (op.op === "bin") {
      const bin = {
        name: "Group",
        attrs: "",
        children: [el("UUID", op.group), el("Name", "Recycle Bin"),
          { name: "Times", attrs: "", children: [el("CreationTime", op.at), el("LastModificationTime", op.at), el("LocationChanged", op.at)] }],
      };
      group(op.parent).node.children.push(bin);
      walked.groups.set(op.group, { node: bin, parent: op.parent, path: "Recycle Bin" });
      set(db.meta, "RecycleBinEnabled", "True");
      set(db.meta, "RecycleBinUUID", op.group);
      set(db.meta, "RecycleBinChanged", op.at);
    } else {
      throw new Error(`unknown op ${op.op}`);
    }
  }
}

const [command, name, ...args] = process.argv.slice(2);

if (command === "iso") {
  console.log(iso(Number(name)));
  process.exit(0);
}

const db = file(name);
const walked = walk(db);

if (command === "entry") {
  const found = walked.entries.get(args[0]);
  console.log(JSON.stringify(found ? {
    group: found.group,
    path: found.path,
    current: version(found.node),
    history: kids(kid(found.node, "History"), "Entry").map(version),
  } : null));
} else if (command === "group") {
  const found = walked.groups.get(args[0]);
  const data = {};
  if (found) for (const item of kids(kid(found.node, "CustomData"), "Item")) data[text(kid(item, "Key"))] = text(kid(item, "Value"));
  console.log(JSON.stringify(found ? {
    name: text(kid(found.node, "Name")),
    parent: found.parent,
    tags: text(kid(found.node, "Tags")),
    searching: text(kid(found.node, "EnableSearching")),
    data,
  } : null));
} else if (command === "meta") {
  console.log(JSON.stringify({
    bin: text(kid(db.meta, "RecycleBinUUID")),
    deleted: kids(kid(db.root, "DeletedObjects"), "DeletedObject").map((d) => text(kid(d, "UUID"))),
    markers41: markers41(db, walked),
  }));
} else if (command === "craft") {
  craft(db, JSON.parse(fs.readFileSync(args[1], "utf8")));
  fs.writeFileSync(args[0], `<?xml version="1.0" encoding="utf-8" standalone="yes"?>\n${db.doc.children.map(serialize).join("")}\n`);
} else {
  throw new Error(`unknown command ${command}`);
}
JS

xml() { node "$(native "$dir/merge-xml.js")" "$@" | tr -d '\r'; }

# KeePassXC's export of $1, kept as $dir/<$2>.xml, whose native path is printed.
exported() {
  local out="$dir/$2.xml"
  kx export "$1" -f xml >"$out" || die "KeePassXC cannot export $(basename "$1")"
  native "$out"
}

# What KeePassXC's export $1 holds of the item named $2 (see uuid), as JSON, or null.
entry_in() { xml entry "$1" "$(uuid "$2")"; }

# That the item named $1 in the export $2 passes the jq test $3; $4 says what failed.
expect_entry() { entry_in "$2" "$1" | jq -e "$3" >/dev/null || die "$4: $(entry_in "$2" "$1")"; }

services_id=$(uuid services)
billing_id=$(uuid billing)
bin_id=$(uuid bin)
file_bin_id=$(uuid file-bin)

# A vault KeePassXC imports, $dir/<$1>.kdbx: under a root group named for $3, groups services, billing and
# Recycle Bin with the bin on, a history limit of $2, and in services one item per further argument, its
# password <name>-0. Every time is in the past, so every change made now is newer. The root group's custom data
# keeps KeePassXC saving KDBX 4, which keypaste merges: it saves an AES-KDF vault holding none as KDBX 3.1 (D-0541).
seed() {
  local name=$1 limit=$2 root=$3 item at='2026-01-01T00:00:00Z'
  shift 3
  {
    cat <<EOF
<?xml version="1.0" encoding="utf-8" standalone="yes"?>
<KeePassFile>
  <Meta>
    <Generator>verify-keepassxc-merge</Generator><DatabaseName>merge</DatabaseName>
    <RecycleBinEnabled>True</RecycleBinEnabled><RecycleBinUUID>${bin_id}</RecycleBinUUID>
    <HistoryMaxItems>${limit}</HistoryMaxItems><HistoryMaxSize>6291456</HistoryMaxSize>
  </Meta>
  <Root>
    <Group>
      <UUID>$(uuid "$root")</UUID><Name>Root</Name>
      <Times><LastModificationTime>${at}</LastModificationTime></Times>
      <CustomData><Item><Key>verify-keepassxc-merge</Key><Value>kdbx4</Value></Item></CustomData>
      <Group>
        <UUID>${services_id}</UUID><Name>services</Name>
        <Times><LastModificationTime>${at}</LastModificationTime></Times>
EOF
    for item in "$@"; do
      cat <<EOF
        <Entry>
          <UUID>$(uuid "$item")</UUID>
          <Times><CreationTime>${at}</CreationTime><LastModificationTime>${at}</LastModificationTime><LocationChanged>${at}</LocationChanged></Times>
          <String><Key>Title</Key><Value>${item}</Value></String>
          <String><Key>Password</Key><Value ProtectInMemory="True">${item}-0</Value></String>
        </Entry>
EOF
    done
    cat <<EOF
      </Group>
      <Group><UUID>${billing_id}</UUID><Name>billing</Name><Times><LastModificationTime>${at}</LastModificationTime></Times></Group>
      <Group><UUID>${bin_id}</UUID><Name>Recycle Bin</Name><Times><LastModificationTime>${at}</LastModificationTime></Times></Group>
    </Group>
  </Root>
</KeePassFile>
EOF
  } >"$dir/$name.xml"
  import_xml "$dir/$name.xml" "$dir/$name.kdbx"
}

# KeePassXC imports the document $1 as the vault $2, under the password $3 or the gate's.
import_xml() {
  local password=${3:-$pw}
  rm -f "$2"
  printf '%s\n%s\n' "$password" "$password" | "$cli" import -q -p "$(native "$1")" "$(native "$2")" >/dev/null \
    || die "keepassxc-cli could not import $(basename "$1")"
}

merge_into() { # target, source
  printf '%s\n' "$pw" | "$cli" merge -q -s "$(native "$1")" "$(native "$2")" >/dev/null || die "keepassxc-cli could not merge $(basename "$2") into $(basename "$1")"
}

# Holds $1 in the app as launch composes it, with a person who approves every request.
hold() {
  release
  holds=$((holds + 1))
  hold_out="$dir/hold-$holds.txt"
  : >"$hold_out"
  exec 7>&-
  exec 7> >(KEYPASTE_DRIVER_PASSWORD="$pw" exec "$drv" hold "$(native "$1")" --approving-prompt >"$hold_out" 2>&1)
  WAIT_SECONDS=60 wait_for 'holding session' "$hold_out"
  if grep -q 'not served' "$hold_out"; then die "the app unlocked $(basename "$1") and did not serve it"; fi
  hold_pid=$(grep 'holding session' "$hold_out" | tail -1 | sed -E 's/.* as process ([0-9]+).*/\1/')
}

release() {
  [ -n "$hold_pid" ] || return 0
  exec 7>&-
  wait_for '^shut down' "$hold_out"
  hold_pid=
  exec 7>/dev/null
}

# Sends $1 to the held app and prints the next line its output gains matching $2.
act() {
  local before count
  before=$(grep -c -- "$2" "$hold_out" || true)
  printf '%s\n' "$1" >&7
  for _ in $(seq 1 300); do
    count=$(grep -c -- "$2" "$hold_out" || true)
    if [ "$count" -gt "$before" ]; then
      grep -- "$2" "$hold_out" | sed -n "$((before + 1))p"
      return 0
    fi
    sleep 0.2
  done
  die "the app never answered '$1' with a line matching '$2'"
}

merges_in() { grep -c '^merged ' "$hold_out" || true; }

# Has the held app compare the file and merge as its folder watch does, and prints the merge's line, or "none".
merge() {
  local before
  before=$(merges_in)
  act merge '^merge end' >/dev/null
  if [ "$(merges_in)" -gt "$before" ]; then grep '^merged ' "$hold_out" | sed -n "$((before + 1))p"; else echo none; fi
}

# The app's edit of $1's password to $2, through its entries screen, printing what the screen said.
edit() { act "edit $1 $2" '^saved \|^refused:'; }

# The merge line the app printed last.
last_merge() { grep '^merged ' "$hold_out" | tail -n 1; }

# G12: KeePassXC opens $1, which is KDBX 4, and 4.1 exactly when its export holds what only 4.1 carries.
checked() {
  local hdr minor wants holding
  hdr=$(header "$1")
  [ "${hdr:0:16}" = "03d9a29a67fb4bb5" ] || die "$(basename "$1") is not a KDBX file after keypaste's merge"
  [ "${hdr:20:4}" = "0400" ] || die "$(basename "$1") is KDBX major 0x${hdr:20:4} after keypaste's merge"
  minor=${hdr:16:4}
  kx db-info "$1" >/dev/null || die "KeePassXC cannot open $(basename "$1") after keypaste's merge"
  wants=$(xml meta "$(exported "$1" "$(basename "$1" .kdbx)-g12")" | jq -r '.markers41')
  holding='holds no'
  if [ "$wants" = true ]; then holding=holds; fi
  case "$wants:$minor" in
    true:0100 | false:0000) ;;
    *) die "$(basename "$1") is KDBX 4 minor 0x${minor} although its export ${holding} 4.1 content" ;;
  esac
}

audit_merges() { if [ -f "$audit" ]; then jq -s '[.[] | select(.tool == "vault_merge")] | length' "$audit"; else echo 0; fi; }

# ------------------------------------------------------------------------------------- G3, measured first
step "G3: KeePassXC edits the version the app saved, which measures whether its round trip keeps a version intact"
seed g3 10 root Epsilon Zeta Eta
v=$dir/g3.kdbx
hold "$v"
[ "$(edit services/Epsilon epsilon-app-g3)" = 'saved services/Epsilon' ] || die "the app's edit of Epsilon was not saved"
kx edit "$v" services/Epsilon -u kpxc-g3a >/dev/null || die "KeePassXC could not edit services/Epsilon"
line=$(merge)
case "$line" in
  'merged updated') echo "    measured: KeePassXC kept the version keypaste saved intact across its own load and save, so lineage holds" ;;
  "merged saved: "*"both changed services/Epsilon"*)
    echo "    measured: KeePassXC changed the version keypaste saved across its own load and save, so lineage falls back to time;"
    echo "    every later case that expects a merge to take KeePassXC's save as it is fails for that reason" ;;
  *) die "the app did not merge KeePassXC's edit of the version it saved: ${line}" ;;
esac
json=$(entry_in "$(exported "$v" g3a)" Epsilon)
jq -e '.current.username == "kpxc-g3a" and .current.password == "epsilon-app-g3" and any(.history[]; .password == "epsilon-app-g3" and .username == "")' <<<"$json" >/dev/null \
  || die "Epsilon is not KeePassXC's edit with the app's version in its history: ${json}"
checked "$v"

step "G3: one item changed by both, the app's change newer, then KeePassXC's"
kx edit "$v" services/Zeta -u kpxc-g3b >/dev/null || die "KeePassXC could not edit services/Zeta"
sleep 2.1
[ "$(edit services/Zeta zeta-app-g3)" = 'saved services/Zeta' ] || die "the app's newer edit of Zeta was not saved"
case "$(last_merge)" in
  *"You and the other program both changed services/Zeta. Your version is current; the other program's is in its history."*) ;;
  *) die "the notice did not name Zeta as changed in both with the app's version current: $(last_merge)" ;;
esac
json=$(entry_in "$(exported "$v" g3b)" Zeta)
jq -e '.current.password == "zeta-app-g3" and .current.username == "" and any(.history[]; .username == "kpxc-g3b")' <<<"$json" >/dev/null \
  || die "Zeta is not the app's newer version with KeePassXC's in its history: ${json}"
checked "$v"

cp "$v" "$dir/g3-fork.kdbx"
[ "$(edit services/Eta eta-app-g3)" = 'saved services/Eta' ] || die "the app's edit of Eta was not saved"
sleep 2.1
kx edit "$dir/g3-fork.kdbx" services/Eta -u kpxc-g3c >/dev/null || die "KeePassXC could not edit its copy of Eta"
cp "$dir/g3-fork.kdbx" "$v"
line=$(merge)
case "$line" in
  *"You and the other program both changed services/Eta. The other program's version is current; yours is in its history."*) ;;
  *) die "the notice did not name Eta as changed in both with KeePassXC's version current: ${line}" ;;
esac
json=$(entry_in "$(exported "$v" g3c)" Eta)
jq -e '.current.username == "kpxc-g3c" and .current.password == "Eta-0" and any(.history[]; .password == "eta-app-g3")' <<<"$json" >/dev/null \
  || die "Eta is not KeePassXC's newer version with the app's in its history: ${json}"
checked "$v"

# ------------------------------------------------------------------------------------- G1
step "G1: KeePassXC's save to one item refuses the app's edit of another, and keypaste saves both once"
seed g1 10 root Alpha Beta
v=$dir/g1.kdbx
hold "$v"
kx edit "$v" services/Beta -u kpxc-g1 >/dev/null || die "KeePassXC could not edit services/Beta"
cp "$v" "$dir/g1-kpxc.kdbx"
merges_before=$(audit_merges)
said=$(edit services/Alpha alpha-app-g1)
[ "$said" = 'saved services/Alpha' ] || die "the app's edit was not saved through the merge: ${said}"
line=$(last_merge)
[ "$line" = "merged saved: Another program saved g1.kdbx, and keypaste merged it with your changes and saved. Your change to services/Alpha is saved." ] \
  || die "the notice did not name the edit as saved: ${line}"
[ "$(audit_merges)" = $((merges_before + 1)) ] || die "keypaste did not record exactly one merge: $(audit_merges) after ${merges_before}"
[ "$(kx show "$v" services/Alpha -a Password)" = alpha-app-g1 ] || die "KeePassXC does not read the app's edit"
[ "$(kx show "$v" services/Beta -a UserName)" = kpxc-g1 ] || die "KeePassXC's own edit is gone"
kept=0
for copy in "$v.backups"/*; do
  if [ -f "$copy" ] && cmp -s "$copy" "$dir/g1-kpxc.kdbx"; then kept=1; fi
done
[ "$kept" = 1 ] || die "the merge's save did not keep KeePassXC's save as a backup"
checked "$v"

# ------------------------------------------------------------------------------------- G2
step "G2: KeePassXC saves and then the app edits"
seed g2 10 root Alpha Beta Gamma Delta
v=$dir/g2.kdbx
hold "$v"
kx edit "$v" services/Beta -u kpxc-g2 >/dev/null || die "KeePassXC could not edit services/Beta"
[ "$(merge)" = 'merged updated' ] || die "the app did not take KeePassXC's save as it is: $(last_merge)"
checked "$v"
[ "$(edit services/Alpha alpha-app-g2)" = 'saved services/Alpha' ] || die "the app's edit after the merge was not saved"
[ "$(kx show "$v" services/Beta -a UserName)" = kpxc-g2 ] || die "KeePassXC's save did not survive the app's edit"
[ "$(kx show "$v" services/Alpha -a Password)" = alpha-app-g2 ] || die "the app's edit is not in the file"

step "G2: the app saves before a KeePassXC copy taken earlier is moved over the file"
cp "$v" "$dir/g2-fork.kdbx"
[ "$(edit services/Gamma gamma-app-g2)" = 'saved services/Gamma' ] || die "the app's second edit was not saved"
sleep 1.1
kx edit "$dir/g2-fork.kdbx" services/Delta -u kpxc-g2-fork >/dev/null || die "KeePassXC could not edit its copy"
cp "$dir/g2-fork.kdbx" "$v"
line=$(merge)
case "$line" in "merged saved: "*) ;; *) die "the app did not merge the older copy moved over the file: ${line}" ;; esac
case "$line" in *"both changed"*) die "an older copy of an item the app saved was named as a conflict: ${line}" ;; esac
[ "$(kx show "$v" services/Gamma -a Password)" = gamma-app-g2 ] || die "the app's saved change did not survive the older copy"
[ "$(kx show "$v" services/Delta -a UserName)" = kpxc-g2-fork ] || die "KeePassXC's newer change in the older copy is not current"
[ "$(kx show "$v" services/Alpha -a Password)" = alpha-app-g2 ] || die "the app's earlier change did not survive the older copy"
checked "$v"

# ------------------------------------------------------------------------------------- G4
step "G4: the open copy merged by keepassxc-cli merge -s against keypaste's merge of the same copy"
seed g4 2 root Alpha Tomb Kept Tie Shared Early Moved Limit Bin1 Bin2
v=$dir/g4.kdbx
# Limit's history as full as the limit of two allows, so the version a merge displaces is one the limit would drop.
cat >"$dir/g4-history.json" <<EOF
[
  {"op": "edit", "entry": "$(uuid Limit)", "keep": true, "strings": {"Password": "x-1"}, "modified": "2026-01-01T00:00:01Z"},
  {"op": "edit", "entry": "$(uuid Limit)", "keep": true, "strings": {"Password": "x-2"}, "modified": "2026-01-01T00:00:02Z"}
]
EOF
xml craft "$(exported "$v" g4-seed)" "$(native "$dir/g4-history.xml")" "$(native "$dir/g4-history.json")"
import_xml "$dir/g4-history.xml" "$v"
hold "$v"
s0=$(exported "$v" g4-s0)
act 'unsaved-edit services/Kept kept-app' '^changed services/Kept' >/dev/null
act 'unsaved-edit services/Tie tie-app' '^changed services/Tie' >/dev/null
act 'unsaved-edit services/Moved moved-app' '^changed services/Moved' >/dev/null
act 'unsaved-edit services/Limit limit-app' '^changed services/Limit' >/dev/null
act 'unsaved-edit services/Shared shared-app' '^changed services/Shared' >/dev/null
act 'unsaved-rename billing billing-app' '^renamed billing' >/dev/null
act 'unsaved-delete services/Bin1' '^deleted services/Bin1' >/dev/null
act "write-open $(native "$dir/g4-open.kdbx")" '^wrote the open copy' >/dev/null
l=$(exported "$dir/g4-open.kdbx" g4-open)
tie_at=$(xml iso "$(entry_in "$l" Tie | jq -r '.current.modified')")
expect_entry Bin1 "$l" ".group == \"${bin_id}\"" "the open copy KeePassXC reads did not recycle Bin1 into the vault's bin"

# KeePassXC's copy, from its export before the app's changes: a newer change, two purges, a change in
# the second of the app's, another version in the second of the one the app's change put in history, a
# change stamped before the base, a move, a group's tags, data and flags, a change older than the app's,
# and a recycle bin of its own.
cat >"$dir/g4-file.json" <<EOF
[
  {"op": "edit", "entry": "$(uuid Alpha)", "keep": true, "strings": {"Password": "alpha-kpxc"}, "modified": "2026-01-07T00:00:00Z"},
  {"op": "purge", "entry": "$(uuid Tomb)", "at": "2026-01-06T00:00:00Z"},
  {"op": "purge", "entry": "$(uuid Kept)", "at": "2026-01-06T00:00:00Z"},
  {"op": "edit", "entry": "$(uuid Tie)", "keep": true, "strings": {"Password": "tie-kpxc"}, "modified": "${tie_at}"},
  {"op": "edit", "entry": "$(uuid Shared)", "keep": true},
  {"op": "history", "entry": "$(uuid Shared)", "like": 0, "replace": true, "strings": {"Password": "h-0-kpxc"}},
  {"op": "edit", "entry": "$(uuid Early)", "keep": true, "strings": {"Password": "early-kpxc"}, "modified": "2025-12-30T00:00:00Z"},
  {"op": "move", "entry": "$(uuid Moved)", "to": "${billing_id}", "location": "2026-01-02T00:00:00Z"},
  {"op": "group", "group": "${billing_id}", "tags": "kpxc-tag", "searching": "false", "data": {"merge.group": "kpxc"}, "modified": "2026-01-02T00:00:00Z"},
  {"op": "edit", "entry": "$(uuid Limit)", "strings": {"Password": "x-kpxc"}, "modified": "2025-12-31T00:00:00Z"},
  {"op": "bin", "group": "${file_bin_id}", "parent": "$(uuid root)", "at": "2026-01-02T00:00:00Z"},
  {"op": "move", "entry": "$(uuid Bin2)", "to": "${file_bin_id}", "location": "2026-01-02T00:00:00Z", "previous": "${services_id}"}
]
EOF
xml craft "$s0" "$(native "$dir/g4-file.xml")" "$(native "$dir/g4-file.json")"
import_xml "$dir/g4-file.xml" "$dir/g4-file.kdbx"
cp "$dir/g4-file.kdbx" "$dir/g4-oracle.kdbx"
cp "$dir/g4-file.kdbx" "$v"
line=$(merge)
case "$line" in "merged saved: "*) ;; *) die "the app did not merge KeePassXC's copy with its own: ${line}" ;; esac
checked "$v"
r=$(exported "$v" g4-keypaste)
merge_into "$dir/g4-oracle.kdbx" "$dir/g4-open.kdbx"
k=$(exported "$dir/g4-oracle.kdbx" g4-keepassxc)
release

step "G4: where the two merges agree"
expect_entry Alpha "$r" '.current.password == "alpha-kpxc"' "keypaste did not take KeePassXC's newer change"
expect_entry Alpha "$k" '.current.password == "alpha-kpxc"' "KeePassXC's own merge did not keep its newer change"
expect_entry Moved "$r" ".current.password == \"moved-app\" and .group == \"${billing_id}\"" "keypaste did not put the app's change where KeePassXC moved the item"
expect_entry Moved "$k" ".current.password == \"moved-app\" and .group == \"${billing_id}\"" "KeePassXC's merge did not put the app's change where it moved the item"
expect_entry Bin2 "$r" ".group == \"${file_bin_id}\"" "keypaste moved KeePassXC's recycled item out of its bin"

step "G4: tombstones applied, where KeePassXC's default merge applies none"
entry_in "$r" Tomb | jq -e '. == null' >/dev/null || die "keypaste brought back the item KeePassXC purged"
xml meta "$r" | jq -e --arg id "$(uuid Tomb)" '.deleted | index($id) != null' >/dev/null || die "keypaste dropped the purge's tombstone"
expect_entry Tomb "$k" '. != null' "KeePassXC's merge applied a deletion, which its default mode does not"

step "G4: a deletion against a change the deleting copy never saw is kept in the bin, where KeePassXC keeps it in place"
expect_entry Kept "$r" ".current.password == \"kept-app\" and .group == \"${file_bin_id}\"" "keypaste did not keep the app's change in the bin"
expect_entry Kept "$k" ".current.password == \"kept-app\" and .group == \"${services_id}\"" "KeePassXC's merge did not keep the item where it was"

step "G4: a tie keeps both versions, where KeePassXC drops the open copy's"
expect_entry Tie "$r" '.current.password == "tie-app" and any(.history[]; .password == "tie-kpxc")' "keypaste did not keep both versions of the tie"
expect_entry Tie "$k" '.current.password == "tie-kpxc" and all(.history[]; .password != "tie-app")' "KeePassXC's merge kept the open copy's version of a tie"

step "G4: versions sharing a second are all kept, where KeePassXC keeps one"
expect_entry Shared "$r" 'any(.history[]; .password == "Shared-0") and any(.history[]; .password == "h-0-kpxc")' "keypaste dropped a version sharing a second"
expect_entry Shared "$k" '[.history[] | select(.password == "Shared-0" or .password == "h-0-kpxc")] | length == 1' "KeePassXC's merge kept two versions of one second"

step "G4: a change only the file made, stamped before the base, is taken by lineage, where KeePassXC keeps the later stamp"
expect_entry Early "$k" '.current.password == "Early-0"' "KeePassXC's merge did not keep the later stamp"
early=$(entry_in "$r" Early | jq -r '.current.password')
case "$early" in
  early-kpxc) echo "    measured: KeePassXC's export and import kept the base's version, so lineage took the file's change" ;;
  Early-0) echo "    measured: KeePassXC's export and import changed the base's version, so lineage could not be shown and time decided, as in KeePassXC" ;;
  *) die "keypaste's merge made Early ${early}" ;;
esac

step "G4: LocationChanged travels with the place, where KeePassXC takes the older with the content it took"
expect_entry Moved "$r" '.current.location == 1767312000' "keypaste did not keep KeePassXC's LocationChanged with the place it moved the item to"
expect_entry Moved "$k" '.current.location != 1767312000' "KeePassXC's merge kept the mover's LocationChanged, where its default mode takes the older"

step "G4: group custom data, tags and flags go with the newer group, where KeePassXC keeps the target's"
group_r=$(xml group "$r" "$billing_id")
group_k=$(xml group "$k" "$billing_id")
jq -e '.name == "billing-app" and .tags == "" and (.data | has("merge.group") | not) and .searching != "false"' <<<"$group_r" >/dev/null \
  || die "keypaste did not take the newer group whole: ${group_r}"
jq -e '.name == "billing-app" and .tags == "kpxc-tag" and .data["merge.group"] == "kpxc"' <<<"$group_k" >/dev/null \
  || die "KeePassXC's merge took the newer group's tags and data, where its default mode keeps the target's: ${group_k}"

step "G4: the displaced version is kept past the history limit, where KeePassXC trims it away"
expect_entry Limit "$r" '.current.password == "limit-app" and any(.history[]; .password == "x-kpxc")' "keypaste trimmed the version its merge displaced"
expect_entry Limit "$k" '.current.password == "limit-app" and all(.history[]; .password != "x-kpxc")' "KeePassXC's merge kept the displaced version past the limit"

step "G4: two recycle bins are reconciled into the file's, where KeePassXC keeps them apart"
xml meta "$r" | jq -e --arg bin "$file_bin_id" '.bin == $bin' >/dev/null || die "keypaste's result does not name the file's recycle bin"
expect_entry Bin1 "$r" ".group == \"${file_bin_id}\"" "keypaste left the open copy's recycled item outside the file's bin"
expect_entry Bin1 "$k" ".group == \"${bin_id}\"" "KeePassXC's merge moved the open copy's recycled item into the file's bin"

# ------------------------------------------------------------------------------------- G5
step "G5: KeePassXC purges an item the app did not change and one it did"
seed g5 10 root Theta Iota
v=$dir/g5.kdbx
hold "$v"
for item in Theta Iota; do
  kx rm "$v" "services/$item" >/dev/null || die "KeePassXC could not recycle ${item}"
  kx rm "$v" "Recycle Bin/$item" >/dev/null || die "KeePassXC could not purge ${item}"
done
[ "$(edit services/Iota iota-app-g5)" = 'saved services/Iota' ] || die "the app's edit of the purged Iota was not saved"
case "$(last_merge)" in
  *"The other program deleted services/Iota after you changed it here. It is in Trash with your change."*) ;;
  *) die "the notice did not name Iota as kept in Trash: $(last_merge)" ;;
esac
json=$(exported "$v" g5)
entry_in "$json" Theta | jq -e '. == null' >/dev/null || die "Theta came back, which the app had not changed"
xml meta "$json" | jq -e --arg id "$(uuid Theta)" '.deleted | index($id) != null' >/dev/null || die "Theta's tombstone is gone"
expect_entry Iota "$json" ".current.password == \"iota-app-g5\" and .group == \"${bin_id}\"" "Iota is not in the bin with the app's change"
[ "$(kx show "$v" 'Recycle Bin/Iota' -a Password)" = iota-app-g5 ] || die "KeePassXC does not read Iota in its bin"
checked "$v"

# ------------------------------------------------------------------------------------- G6
step "G6: the app restores an item KeePassXC purged, and KeePassXC recycles an item the app edits"
seed g6 10 root Beta Gamma
v=$dir/g6.kdbx
hold "$v"
kx rm "$v" services/Gamma >/dev/null || die "KeePassXC could not recycle Gamma"
[ "$(merge)" = 'merged updated' ] || die "the app did not take KeePassXC's recycling: $(last_merge)"
kx rm "$v" 'Recycle Bin/Gamma' >/dev/null || die "KeePassXC could not purge Gamma"
said=$(act 'restore Gamma' 'is back \|^refused:')
case "$said" in
  *'is back in services'*) restored=services/Gamma ;;
  *'is back at the root'*) restored=Gamma ;;
  *) die "the app did not restore Gamma: ${said}" ;;
esac
[ "$(kx show "$v" "$restored" -a Password)" = Gamma-0 ] || die "the restore did not survive KeePassXC's purge"
checked "$v"
kx rm "$v" services/Beta >/dev/null || die "KeePassXC could not recycle Beta"
[ "$(edit services/Beta beta-app-g6)" = 'saved services/Beta' ] || die "the app's edit of the recycled Beta was not saved"
[ "$(kx show "$v" 'Recycle Bin/Beta' -a Password)" = beta-app-g6 ] || die "Beta is not in the bin with the app's edit"
checked "$v"

# ------------------------------------------------------------------------------------- G7
step "G7: KeePassXC attaches a file while the app edits another item, and a conflict puts an attachment in history"
seed g7 10 root Alpha Doc Ctl
v=$dir/g7.kdbx
printf 'first attachment\000\001\002 bytes\n' >"$dir/first.bin"
head -c 4096 /dev/urandom >"$dir/second.bin"
kx attachment-import "$v" services/Doc file.bin "$(native "$dir/first.bin")" >/dev/null || die "KeePassXC could not attach to Doc"
hold "$v"
kx attachment-import "$v" services/Ctl file.bin "$(native "$dir/second.bin")" >/dev/null || die "KeePassXC could not attach to Ctl"
[ "$(edit services/Alpha alpha-app-g7)" = 'saved services/Alpha' ] || die "the app's edit beside KeePassXC's attachment was not saved"
kx attachment-export "$v" services/Ctl file.bin "$(native "$dir/ctl.out")" >/dev/null || die "KeePassXC cannot export Ctl's attachment"
cmp -s "$dir/ctl.out" "$dir/second.bin" || die "Ctl's attachment changed through the merge"
checked "$v"
kx attachment-import "$v" services/Doc file.bin "$(native "$dir/second.bin")" -f >/dev/null || die "KeePassXC could not replace Doc's attachment"
sleep 2.1
[ "$(edit services/Doc doc-app-g7)" = 'saved services/Doc' ] || die "the app's newer edit of Doc was not saved"
kx attachment-export "$v" services/Doc file.bin "$(native "$dir/doc.out")" >/dev/null || die "KeePassXC cannot export Doc's attachment"
cmp -s "$dir/doc.out" "$dir/first.bin" || die "Doc's current attachment is not the app's version's"
json=$(exported "$v" g7)
ctl_ref=$(entry_in "$json" Ctl | jq -r '.current.attachments[0].ref')
doc_ref=$(entry_in "$json" Doc | jq -r '.current.attachments[0].ref')
[ "$ctl_ref" != "$doc_ref" ] || die "the two attachments read as the same bytes"
# KeePassXC numbers an export's attachments by their bytes, so a version sharing Ctl's number holds Ctl's bytes.
entry_in "$json" Doc | jq -e --arg ref "$ctl_ref" 'any(.history[]; any(.attachments[]; .ref == $ref))' >/dev/null \
  || die "Doc's history does not hold KeePassXC's attachment byte for byte: $(entry_in "$json" Doc)"
checked "$v"

# ------------------------------------------------------------------------------------- G8
step "G8: a revoked token stays revoked after KeePassXC merges in a copy still holding it"
cat >"$dir/g8.xml" <<EOF
<?xml version="1.0" encoding="utf-8" standalone="yes"?>
<KeePassFile>
  <Meta><Generator>verify-keepassxc-merge</Generator><DatabaseName>merge</DatabaseName></Meta>
  <Root>
    <Group>
      <UUID>$(uuid root)</UUID><Name>Root</Name>
      <CustomData><Item><Key>verify-keepassxc-merge</Key><Value>kdbx4</Value></Item></CustomData>
      <Group>
        <UUID>${services_id}</UUID><Name>services</Name>
        <Entry>
          <UUID>$(uuid Stripe)</UUID>
          <Tags>env:billing</Tags>
          <String><Key>Title</Key><Value>Stripe</Value></String>
          <String><Key>STRIPE_KEY</Key><Value ProtectInMemory="True">sk-merge-g8</Value></String>
        </Entry>
      </Group>
    </Group>
  </Root>
</KeePassFile>
EOF
v=$dir/g8.kdbx
import_xml "$dir/g8.xml" "$v"
token=$(printf '%s\n' "$pw" | "$kp" token create gate-merge --scope 'read:billing/dev/*' --vault "$(native "$v")" 2>"$dir/g8-token.err" | tr -d '\r') \
  || die "keypaste token create failed: $(cat "$dir/g8-token.err")"
[ -n "$token" ] || die "keypaste token create printed no token"

# $1 names the case. With "runs" as $2 the token's run starts its child; otherwise it is refused, nothing starts, and the owner records why.
token_run() {
  local logged out status
  logged=$(lines_in "$audit")
  set +e
  out=$(KEYPASTE_TOKEN="$token" "$kp" run --token env --vault "$(native "$v")" billing -- "$kp" --version 2>"$dir/g8-run.err" | tr -d '\r')
  status=$?
  set -e
  if [ "${2:-}" = runs ]; then
    if [ "$status" -ne 0 ] || [ -z "$out" ]; then die "$1: the token did not run: $(cat "$dir/g8-run.err")"; fi
    return 0
  fi
  [ "$status" -ne 0 ] || die "$1: the revoked token ran: ${out}"
  [ -z "$out" ] || die "$1: a child started for the revoked token: ${out}"
  lines_after "$audit" "$logged" | jq -e -s 'any(.[]; .method == "token" and .decision == "denied" and (.reason | contains("not valid")))' >/dev/null \
    || die "$1: the refusal was not recorded as a token the vault does not know: $(lines_after "$audit" "$logged")"
}

hold "$v"
token_run "before the revocation" runs
release
cp "$v" "$dir/g8-stale.kdbx"
printf '%s\n' "$pw" | "$kp" token revoke gate-merge --vault "$(native "$v")" >/dev/null 2>"$dir/g8-revoke.err" \
  || die "keypaste token revoke failed: $(cat "$dir/g8-revoke.err")"
hold "$v"
merge_into "$v" "$dir/g8-stale.kdbx"
line=$(merge)
case "$line" in 'merged updated' | 'merged saved: '*) ;; *) die "the app did not merge KeePassXC's merge: ${line}" ;; esac
checked "$v"
token_run "after KeePassXC merged in the copy still holding the token"
cp "$dir/g8-stale.kdbx" "$v"
line=$(merge)
case "$line" in 'merged saved: '*) ;; *) die "the app did not merge the copy still holding the token: ${line}" ;; esac
checked "$v"
token_run "after that copy was moved over the file"

# ------------------------------------------------------------------------------------- G11
step "G11: another vault is not merged and the notice says so"
seed g11 10 root Alpha Beta
seed g11-other 10 another-root Other
v=$dir/g11.kdbx
hold "$v"
cp "$v" "$dir/g11-mine.kdbx"
cp "$dir/g11-other.kdbx" "$v"
[ "$(merge)" = 'merged refused AnotherVault: the file now holds another vault' ] || die "another vault was not refused: $(last_merge)"
said=$(act notice '^notice ')
[ "$said" = "notice Another program saved g11.kdbx, and keypaste could not merge it: the file now holds another vault. Agents are refused until you reload it." ] \
  || die "the notice did not say the file holds another vault: ${said}"
[ "$(merge)" = none ] || die "the app merged the same refused bytes again: $(last_merge)"
cmp -s "$v" "$dir/g11-other.kdbx" || die "a refused merge wrote over the other vault"

step "G11: a truncated file is refused, and the complete one then merged"
kx edit "$dir/g11-mine.kdbx" services/Beta -u kpxc-g11 >/dev/null || die "KeePassXC could not edit the vault's own copy"
size=$(wc -c <"$dir/g11-mine.kdbx" | tr -d ' ')
head -c $((size / 2)) "$dir/g11-mine.kdbx" >"$v"
case "$(merge)" in 'merged refused Unreadable: '*) ;; *) die "a truncated file was not refused: $(last_merge)" ;; esac
cp "$dir/g11-mine.kdbx" "$v"
case "$(merge)" in 'merged updated' | 'merged saved: '*) ;; *) die "the complete file was not merged after the truncated one: $(last_merge)" ;; esac
[ "$(kx show "$v" services/Beta -a UserName)" = kpxc-g11 ] || die "KeePassXC's save is not in the merged file"
checked "$v"

step "G11: KeePassXC changes the password, and the app locks"
kx export "$v" -f xml >"$dir/g11-rekeyed.xml" || die "KeePassXC cannot export the vault"
import_xml "$dir/g11-rekeyed.xml" "$dir/g11-rekeyed.kdbx" "$pw-changed"
cp "$dir/g11-rekeyed.kdbx" "$v"
case "$(merge)" in 'merged refused KeyRefused: '*) ;; *) die "a file the unlock's key no longer opens was not refused: $(last_merge)" ;; esac
[ "$(act status '^status ')" = 'status locked' ] || die "the app did not lock when another program changed the password"
release

# ------------------------------------------------------------------------------------- NEGATIVE CONTROL
step "NEGATIVE CONTROL: the readers and comparisons must be able to fail"
entry_in "$r" Nobody | jq -e '. == null' >/dev/null || die "an item nothing made was found"
if entry_in "$r" Tie | jq -e '.current.password == "tie-kpxc"' >/dev/null; then die "a deliberately wrong expectation matched"; fi
if jq -e '.tags == "kpxc-tag"' <<<"$group_r" >/dev/null; then die "the group comparison is not comparing"; fi

printf '\nMERGE GATE PASSED on %s: keypaste merged every save KeePassXC made, kept what the rules keep, and KeePassXC opened every result.\n' \
  "$("$cli" --version | tr -d '\r')"

#!/usr/bin/env bash
# Holds release-targets.json, the workflows, the csprojs and the download pages to one answer about
# what keypaste ships (docs/STEPS.md R.0a, docs/RELEASE.md requirement 1).
#
# Before this file there was no such answer. The four CLI targets were written down independently in
# release.yml's build matrix, its post-transport asset count, its publication allowlist, four
# csprojs' RuntimeIdentifiers, verify-release-destination.sh's fixture, the README install blocks,
# site/public/index.html and docs/RELEASE.md's table. Adding one was five edits with nothing holding
# them together, and nothing at all could fail when they disagreed.
#
# THE DISTINCTION THIS FILE RESTS ON. release-targets.json holds two different records and they must
# not be confused:
#
#   .components[].targets  the ADVERTISED set - what the pipeline builds. It drives the matrices.
#   .published             an append-only record of releases that actually happened.
#
# The download pages advertise `.published`, never the tag. That is what stops a release candidate
# demanding rc filenames in the README: a candidate carries -rc.1 through binaries, archives and the
# changelog while README keeps naming 0.1.0, because nothing has been published. docs/RELEASE.md
# requirement 5's "keep the last verified release advertised until its replacement passes" is the
# same rule seen from the other side, and it falls out of the shape rather than being a case here.
#
# WHAT CATCHES A BAD EDIT TO THE DEFINITION ITSELF. If the matrix, the asset count and the allowlist
# all read one file, dropping a target makes all three agree and it vanishes from a release with
# everything green. So:
#
#   - `published` is append-only, checked against GIT HISTORY, not only against itself. The in-file
#     rule (every published rid is still advertised) is one commit away from being edited around;
#     prior revisions of this file are not.
#   - `targets` and `source_only` must be DISJOINT. "An added target fails at build time" only
#     covers typos: osx-x64, win-arm64 and linux-musl-x64 are real RIDs that would build green on a
#     real runner and publish. Disjointness is what refuses those three by name.
#   - An advertised target must CARRY ITS EVIDENCE - an OS floor with a named way it was
#     established, an install check or a written reason it has none. Silence is not an answer, so
#     adding a target is a wide edit rather than a one-line one.
#
# WHAT IT CANNOT HOLD, said here rather than left to be assumed:
#
#   - THE LIVE SITE. site/ deploys by hand (`wrangler deploy`; there is no site workflow). This
#     reads site/public/index.html at the checked-out ref, so keypaste.com can serve something this
#     already passed and nothing here would notice. Checking the public origin is publication
#     evidence - docs/RELEASE.md requirements 4 and 7, owned by R.0b/R.0c.
#   - AUTHENTICITY. --with-public-origin proves a published asset is still fetchable, not that it is
#     the bytes this project produced. That is O-0010 and 3.8.
#   - ORDERING. docs/STEPS.md 3.9a-d own adding targets and each needs R.1. A definition edit that
#     adds one early contradicts the plan, and review catches that, not a script.
#
# Usage:
#   verify-release-matrix.sh                       fixtures, repository and git history
#   verify-release-matrix.sh --with-public-origin  those, plus an anonymous fetch of published assets
#
# --with-public-origin is run by install.yml alone, which is already weekly-cron plus dispatch and
# already network-bound. ci.yml and release.yml's guard deliberately do NOT run it: a Cloudflare
# blip must not redden main or block a tag, and an unreachable origin is not evidence about this
# file. It tells the two apart with a positive control, the way publish-release.sh does.
#
# NEGATIVE CONTROL: phase F runs the validator with its length check removed and requires it to
# ACCEPT a definition whose target list is null. If the weakened one refuses too, this gate fails: a
# fixture that cannot reproduce the condition proves nothing about the check (D-0043).
set -euo pipefail

readonly DEFINITION="${KEYPASTE_RELEASE_DEFINITION:-release-targets.json}"
readonly PROPS="Directory.Build.props"
# docs/RELEASE.md owns what a floor_evidence value MEANS; this file owns what each one requires.
# Split that way on purpose: the definition carried the values and nothing carried their meaning,
# so "cited" could have been read as observation by anyone who never opened the script. The two
# lists are checked against each other in both directions below, so neither can grow alone.
readonly RELEASE_DOC="docs/RELEASE.md"
readonly FLOOR_RULES="container-check runner-image unverified cited none"

die() { echo "::error::$*" >&2; exit 1; }

MODE="${1:-}"
case "$MODE" in
  ""|--with-public-origin) ;;
  *) die "usage: verify-release-matrix.sh [--with-public-origin]" ;;
esac

command -v jq >/dev/null 2>&1 || die "no jq; this gate reads a JSON contract and cannot run without it"
[ -f "$DEFINITION" ] || die "no release definition at $DEFINITION"

PROBLEMS=()
note() { PROBLEMS+=("$*"); }

# Windows jq writes CRLF, and a CR riding on a key name turns .components."app" into a lookup that
# matches nothing - which reads exactly like a component with no targets. So this gate refused its
# own definition on the machine it is developed on and would have passed on every runner, which is
# the wrong way round for a check whose whole purpose is to be run here. Only the LAST key escapes
# it, because command substitution strips one trailing newline and nothing else.
#
# Stripped at the one place every read goes through rather than at each call site, because a call
# site is a thing somebody adds without.
CR="$(printf '\r')"
jqr() { command jq -r "$@" | tr -d "$CR"; }

# Repository files are read the same way: .editorconfig does not promise LF for every one of them.
noc() { tr -d "$CR"; }

# A jq read that must produce a NUMBER. D-0106: jq 1.6 exits 0 on no output, so a check resting on
# `jq -e` failing is a check that passes on the emptiest possible answer. Require digits instead.
count_of() {
  local file="$1" filter="$2" n
  n="$(jqr "$filter // empty | length" "$file" 2>/dev/null || true)"
  case "$n" in
    "" | *[!0-9]*) echo "" ;;
    *) echo "$n" ;;
  esac
}

json_array() { jqr "$2 // empty | .[]" "$1" 2>/dev/null || true; }

# A comment naming a flag is prose about it, not a use of it, and these headers quote the very flags
# asserted present.
code_of() { grep -vE '^[[:space:]]*#' "$1" | noc; }

# Space-delimited membership, so "$rid" never matches a longer neighbour.
holds() {
  local haystack=" $1 " needle="$2"
  case "$haystack" in *" $needle "*) return 0 ;; esac
  return 1
}

flatten() { tr "\n" " "; }

# The permitted floor_evidence values, read out of docs/RELEASE.md rather than restated here, so
# the table a reader is sent to is the table the gate obeys. The header cell says `floor_evidence`,
# which carries an underscore and therefore cannot be mistaken for one of its own values.
floor_vocab_from_doc() {
  local doc="${1:-$RELEASE_DOC}"
  [ -f "$doc" ] || return 0
  awk '/^## Floor evidence/ { inside = 1; next } inside && /^## / { inside = 0 } inside' "$doc" \
    | noc | sed -n 's/^| `\([a-z][a-z-]*\)` |.*/\1/p'
}
FLOOR_VOCAB="$(floor_vocab_from_doc | flatten)"


# ---------------------------------------------------------------------------
# 1. The definition, judged on its own. Fixtures drive exactly this function.
# ---------------------------------------------------------------------------
validate_definition() {
  local def="$1" weakened="${2:-}"
  local before=${#PROBLEMS[@]}

  jq -e . "$def" >/dev/null 2>&1 || { note "$def is not parseable JSON"; return 1; }

  [ "$(jqr '.schema // empty' "$def")" = "1" ] || note "schema is not 1"

  local components
  components="$(jqr '.components // {} | keys[]' "$def" 2>/dev/null || true)"
  [ -n "$components" ] || { note "no components"; return 1; }

  local source_only
  source_only="$(json_array "$def" '.source_only' | flatten)"

  local c
  for c in $components; do
    local n
    n="$(count_of "$def" ".components.\"$c\".targets")"

    # THE check the negative control removes. A definition whose targets are null or missing reads
    # as an empty array to fromJSON, GitHub skips the job, skips publish through needs:, and reports
    # the workflow green - a release that built nothing and said so nowhere.
    if [ "$weakened" != "--weakened" ]; then
      [ -n "$n" ] || { note "$c: target count is not a number"; continue; }
      [ "$n" -ge 1 ] || { note "$c: no targets"; continue; }
    else
      [ -n "$n" ] || n=0
    fi

    local rids
    rids="$(json_array "$def" ".components.\"$c\".targets | map(.rid)" | flatten)"

    # One jq call for the whole component rather than one per field. Nine spawns per target across
    # nineteen fixtures is about fifteen hundred processes, which costs a minute on Windows and made
    # the gate something a person would rather not run - and a gate nobody runs is D-0105 again.
    #
    # Indexed rather than selected by rid: two targets sharing one rid is a case this has to name,
    # and `select(.rid == ...)` would answer about both of them at once and report the collision as
    # a nonsense field value instead.
    local seen="" rid i runner archive floor evidence caveat citation install absent advertised origin
    origin="$(jqr ".components.\"$c\".origin // empty" "$def")"

    # Unit separator rather than @tsv: a tab is IFS *whitespace*, so bash collapses runs of them and
    # an absent os_floor beside an absent caveat silently shifts every later field one place left.
    while IFS=$'\037' read -r i rid runner archive floor evidence caveat citation install absent advertised; do
      [ -n "$i" ] || continue
      if [ -z "$rid" ]; then
        note "$c: target $i has no rid"
        continue
      fi
      if holds "$seen" "$rid"; then note "$c: $rid listed twice"; fi
      seen="$seen $rid"

      [ -n "$runner" ] || note "$c/$rid: no runner"
      [ -n "$archive" ] || note "$c/$rid: no archive format"

      # A target sitting in source_only is not a typo a build failure would catch: osx-x64,
      # win-arm64 and linux-musl-x64 are valid RIDs that would build and publish.
      if holds "$source_only" "$rid"; then
        note "$c/$rid: advertised and listed source_only; those sets must be disjoint"
      fi

      # An OS floor is a claim to a stranger about their machine. It carries how it was established
      # or it is not made at all. WHICH values exist is docs/RELEASE.md's answer, read at run time;
      # WHAT each one must carry is this block's. Neither half is worth much alone: the definition
      # held the values and nothing held their meaning, so `cited` - which is a citation and
      # explicitly not an observation - could be read as proof by anyone who never opened this file.
      if [ -n "$FLOOR_VOCAB" ] && ! holds "$FLOOR_VOCAB" "$evidence"; then
        note "$c/$rid: floor_evidence '$evidence' is not defined in $RELEASE_DOC"
      fi
      case "$evidence" in
        container-check | runner-image | unverified | cited)
          [ -n "$floor" ] || note "$c/$rid: floor_evidence $evidence but no os_floor" ;;
        none)
          [ -z "$floor" ] || note "$c/$rid: os_floor '$floor' with floor_evidence none" ;;
      esac

      # A floor no run backs carries the sentence that says so, and a cited one carries what it
      # cites. Both are published verbatim, which is what makes deleting either from a page visible.
      case "$evidence" in
        unverified | cited)
          [ -n "$caveat" ] || note "$c/$rid: floor_evidence $evidence with no floor_caveat to publish" ;;
      esac
      if [ "$evidence" = "cited" ] && [ -z "$citation" ]; then
        note "$c/$rid: floor_evidence cited with no floor_citation to point at"
      fi

      if [ -z "$install" ] && [ -z "$absent" ]; then
        note "$c/$rid: no install_check and no written reason it has none"
      fi

      if [ "$advertised" = "true" ] && [ -z "$origin" ]; then
        note "$c/$rid: advertised but the component has no origin to advertise"
      fi
    done <<TARGETS
$(jqr ".components.\"$c\".targets // [] | to_entries[] | [
     (.key | tostring),
     (.value.rid // \"\"), (.value.runner // \"\"), (.value.archive // \"\"),
     (.value.os_floor // \"\"), (.value.floor_evidence // \"\"), (.value.floor_caveat // \"\"),
     (.value.floor_citation // \"\"),
     (.value.install_check // \"\"), (.value.install_check_absent // \"\"),
     (.value.advertised | tostring)
   ] | join(\"\u001f\")" "$def" 2>/dev/null || true)
TARGETS

    # Every RID the csproj declares is either packaged or explained. Silence would let a declared
    # target quietly stop being built.
    local declared reason
    declared="$(json_array "$def" ".components.\"$c\".runtime_identifiers" | flatten)"
    for rid in $declared; do
      if holds "$rids" "$rid"; then continue; fi
      reason="$(jqr ".components.\"$c\".declared_not_packaged[\"$rid\"] // empty" "$def")"
      [ -n "$reason" ] \
        || note "$c/$rid: declared in runtime_identifiers, not packaged, and no reason recorded"
    done
    for rid in $rids; do
      holds "$declared" "$rid" || note "$c/$rid: packaged but not in runtime_identifiers"
    done
  done

  # published: unique versions, and every rid it names is still advertised.
  local pubs
  pubs="$(count_of "$def" '.published')"
  [ -n "$pubs" ] || { note "published is not a list"; return 1; }
  [ "$pubs" -ge 1 ] || note "published is empty; the docs would have nothing to advertise"

  local versions="" i=0
  while [ "$i" -lt "$pubs" ]; do
    local v pc prid
    v="$(jqr ".published[$i].version // empty" "$def")"
    pc="$(jqr ".published[$i].component // empty" "$def")"
    if [ -z "$v" ]; then
      note "published[$i] has no version"
    else
      if holds "$versions" "$v"; then note "published lists $v twice"; fi
      versions="$versions $v"
    fi
    if [ -n "$pc" ]; then
      local advertised_rids
      advertised_rids="$(json_array "$def" ".components.\"$pc\".targets | map(.rid)" | flatten)"
      for prid in $(json_array "$def" ".published[$i].rids"); do
        holds "$advertised_rids" "$prid" \
          || note "$v published $prid and it is no longer an advertised $pc target"
      done
    fi
    i=$((i + 1))
  done

  local ads
  ads="$(count_of "$def" '.published | map(select(.advertised == true))')"
  if [ -z "$ads" ] || [ "$ads" -lt 1 ]; then
    note "no published release is marked advertised; the download pages would have no version"
  fi

  [ ${#PROBLEMS[@]} -eq "$before" ]
}

# The version and origin the download pages must name: the last published entry marked advertised.
# Not the tag, and not simply the last entry - 0.1.0-rc.1 is published and deliberately unadvertised.
advertised_version() { jqr '[.published[] | select(.advertised == true)] | last | .version' "$1"; }
advertised_origin() { jqr '[.published[] | select(.advertised == true)] | last | .origin' "$1"; }

archive_name() {
  local def="$1" c="$2" rid="$3" version="$4" ext
  ext="$(jqr ".components.\"$c\".targets[] | select(.rid == \"$rid\") | .archive" "$def")"
  jqr ".components.\"$c\".archive_pattern" "$def" \
    | sed -e "s/{version}/$version/g" -e "s/{rid}/$rid/g" -e "s/{ext}/$ext/g"
}

# ---------------------------------------------------------------------------
# 2. The definition against the repository: workflows, csprojs, documents.
# ---------------------------------------------------------------------------

# The RIDs a workflow will actually build. Two shapes are legitimate and each is checked as itself:
# a literal matrix, compared exactly; or a matrix derived from the definition, in which case
# equality holds by construction and what must be true is that no literal matrix survives beside it.
workflow_rids() {
  grep -oE "^[[:space:]]*-?[[:space:]]*rid:[[:space:]]*[A-Za-z0-9._-]+[[:space:]]*$" "$1" 2>/dev/null \
    | sed -E "s/.*rid:[[:space:]]*//; s/[[:space:]]*$//" | sort -u | flatten || true
}

validate_workflows() {
  local def="$1" root="$2" c workflow literal expected
  for c in $(jqr '.components | keys[]' "$def"); do
    workflow="$(jqr ".components.\"$c\".workflow" "$def")"
    if [ ! -f "$root/$workflow" ]; then
      note "$c: no workflow at $workflow"
      continue
    fi

    expected="$(json_array "$def" ".components.\"$c\".targets | map(.rid)" | sort -u | flatten)"
    literal="$(workflow_rids "$root/$workflow")"

    if [ -n "$literal" ]; then
      if [ "$literal" != "$expected" ]; then
        note "$workflow builds [$literal] and the definition advertises [$expected]"
      fi
    else
      grep -qF "release-targets.json" "$root/$workflow" \
        || note "$workflow has neither a literal rid matrix nor a read of release-targets.json"
    fi
  done
}

# install.yml keeps a literal matrix rather than reading the definition, because deriving three
# rows would cost a whole extra job on a weekly workflow. Held rather than driven, then: an
# advertised target that records an install check has a row there, on the runner the definition
# names - so a target cannot be advertised on the download page and quietly go untested.
validate_install_jobs() {
  local def="$1" root="$2" c rid check runner
  # Assigned separately: bash expands every word of a `local` before assigning any of them, so
  # "$root" in the same statement is unbound under set -u.
  local workflow="$root/.github/workflows/install.yml"
  [ -f "$workflow" ] || { note "no install workflow at .github/workflows/install.yml"; return 1; }

  for c in $(jqr '.components | keys[]' "$def"); do
    while IFS=$'\037' read -r rid check runner; do
      [ -n "$rid" ] || continue
      [ -n "$check" ] || continue
      grep -qE "target:[[:space:]]*$check\$" "$workflow" \
        || note "install.yml has no '$check' row, and $c/$rid is advertised with that install check"
      [ -n "$runner" ] \
        || { note "$c/$rid: an install check with no install_runner recorded"; continue; }
      grep -qE "os:[[:space:]]*$runner\$" "$workflow" \
        || note "install.yml does not install $c/$rid on $runner, which the definition names"
    done <<INSTALLS
$(jqr ".components.\"$c\".targets // [] | .[]
     | select(.advertised == true)
     | [ (.rid // \"\"), (.install_check // \"\"), (.install_runner // \"\") ]
     | join(\"\u001f\")" "$def" 2>/dev/null || true)
INSTALLS
  done
}

validate_projects() {
  local def="$1" root="$2" c project declared actual union=""
  for c in $(jqr '.components | keys[]' "$def"); do
    declared="$(json_array "$def" ".components.\"$c\".runtime_identifiers" | tr " " "\n" | sort -u | flatten)"
    union="$union $declared"
    for project in $(json_array "$def" ".components.\"$c\".projects"); do
      if [ ! -f "$root/$project" ]; then
        note "$c: no project at $project"
        continue
      fi
      actual="$(grep -oE "<RuntimeIdentifiers>[^<]*</RuntimeIdentifiers>" "$root/$project" \
        | sed -E "s#</?RuntimeIdentifiers>##g" | tr ";" "\n" | sort -u | flatten)"
      [ "$actual" = "$declared" ] \
        || note "$project declares [$actual] and the definition says [$declared]"
    done
  done

  union="$(echo "$union" | tr " " "\n" | grep -v "^$" | sort -u | flatten)"
  for project in $(json_array "$def" '.shared_projects'); do
    [ -f "$root/$project" ] || { note "no shared project at $project"; continue; }
    actual="$(grep -oE "<RuntimeIdentifiers>[^<]*</RuntimeIdentifiers>" "$root/$project" \
      | sed -E "s#</?RuntimeIdentifiers>##g" | tr ";" "\n" | sort -u | flatten)"
    [ "$actual" = "$union" ] \
      || note "$project declares [$actual]; every component's RIDs together are [$union]"
  done
}

# Floor-shaped claims a download page can make. Anything matching one of these must be a value the
# definition holds - which is how "macOS 14 or later" appearing in the README turns this red rather
# than becoming true by being written down.
readonly FLOOR_PATTERNS="glibc [0-9]+\.[0-9]+|macOS [0-9]+(\.[0-9]+)? or (later|newer)|Windows [0-9]+ or (later|newer)"

validate_documents() {
  local def="$1" root="$2" version origin c rid page name claim
  version="$(advertised_version "$def")"
  origin="$(advertised_origin "$def")"
  [ -n "$version" ] && [ "$version" != "null" ] || { note "no advertised version to hold the pages to"; return 1; }

  # Every advertised target with an install check has a block naming its archive, on each page the
  # definition says carries that target. Which pages carry which target is a recorded fact rather
  # than an assumption: site/public/index.html deliberately publishes macOS and Linux and sends
  # Windows, Intel Macs and Alpine to the README, and a check that demanded a Windows block there
  # would be wrong about the product rather than about the page.
  for c in $(jqr '.components | keys[]' "$def"); do
    for rid in $(json_array "$def" ".components.\"$c\".targets | map(select(.advertised == true) | .rid)"); do
      local check target
      target=".components.\"$c\".targets[] | select(.rid == \"$rid\")"
      check="$(jqr "$target | if .install_check == null then \"\" else .install_check end" "$def")"
      [ -n "$check" ] || continue
      name="$(archive_name "$def" "$c" "$rid" "$version")"

      local carried
      carried="$(json_array "$def" "[$target] | map(.advertised_on // []) | add")"
      [ -n "$carried" ] || note "$c/$rid: advertised with an install check and no page recorded as carrying it"

      for page in $carried; do
        [ -f "$root/$page" ] || { note "no page at $page, which is recorded as carrying $rid"; continue; }
        grep -qF "$name" "$root/$page" \
          || note "$page does not name $name, the advertised $rid asset"
        grep -qF "<!-- install:$check -->" "$root/$page" \
          || note "$page has no <!-- install:$check --> sentinel, so nothing can check that block"
        grep -qF "$origin" "$root/$page" \
          || note "$page does not name the advertised origin $origin"
      done
    done
  done

  # A floor claim on a page is a value in the definition or it is not made.
  for page in README.md site/public/index.html docs/RELEASE.md; do
    [ -f "$root/$page" ] || continue
    while IFS= read -r claim; do
      [ -n "$claim" ] || continue
      jq -e --arg c "$claim" '[.components[].targets[].os_floor] | index($c)' "$def" >/dev/null 2>&1 \
        || note "$page claims '$claim' and no target in the definition holds that floor"
    done <<EOF
$(grep -ohE "$FLOOR_PATTERNS" "$root/$page" 2>/dev/null | sort -u)
EOF
  done

  # docs/RELEASE.md owns the floor_evidence vocabulary, so the two lists are held against each
  # other in BOTH directions. A value the table defines and this gate has no rule for is a rule
  # nobody wrote; a rule here the table does not define is a meaning nobody published. Either way
  # the vocabulary has one owner and half its content lives somewhere else, which is the state this
  # check exists to make impossible.
  local doc_vocab v
  doc_vocab="$(floor_vocab_from_doc "$root/$RELEASE_DOC" | flatten)"
  if [ -z "$doc_vocab" ]; then
    note "$RELEASE_DOC defines no floor_evidence values, so nothing owns what they mean"
  else
    for v in $FLOOR_RULES; do
      holds "$doc_vocab" "$v" \
        || note "this gate has a rule for floor_evidence '$v' that $RELEASE_DOC does not define"
    done
    for v in $doc_vocab; do
      holds "$FLOOR_RULES" "$v" \
        || note "$RELEASE_DOC defines floor_evidence '$v' and this gate has no rule for it"
    done
    for v in $(jqr '[.components[].targets[].floor_evidence] | unique[]' "$def"); do
      holds "$doc_vocab" "$v" \
        || note "the definition uses floor_evidence '$v', which $RELEASE_DOC does not define"
    done
  fi

  # Every page that advertises a target STATES that target's floor, and where no run backs that
  # floor states that too. Required presence, not a conditional: the one-directional check above
  # only catches a page inventing a floor and would be perfectly happy with a page that had quietly
  # stopped mentioning one. Deleting the sentence and deleting the paragraph it lives in have to
  # fail the same way, or the check is satisfied by silence.
  #
  # Scoped to the pages a target is advertised ON rather than to all of them, because they do not
  # advertise the same set: site/public/index.html sends Windows readers to the README, and neither
  # page has any business carrying a desktop floor while nothing desktop can be downloaded.
  for c in $(jqr '.components | keys[]' "$def"); do
    for rid in $(json_array "$def" ".components.\"$c\".targets | map(select(.advertised == true) | .rid)"); do
      local target floor evidence caveat pages
      target=".components.\"$c\".targets[] | select(.rid == \"$rid\")"
      floor="$(jqr "$target | .os_floor // empty" "$def")"
      [ -n "$floor" ] || continue
      evidence="$(jqr "$target | .floor_evidence // empty" "$def")"
      caveat="$(jqr "$target | .floor_caveat // empty" "$def")"
      pages="$(json_array "$def" "[$target] | map(.advertised_on // []) | add")"
      for page in $pages; do
        [ -f "$root/$page" ] || continue
        grep -qF "$floor" "$root/$page" \
          || note "$page advertises $rid and no longer states its floor: $floor"
        case "$evidence" in
          unverified | cited)
            grep -qF "$caveat" "$root/$page" \
              || note "$page states the $rid floor without the caveat that qualifies it: $caveat" ;;
        esac
      done
    done
  done

  # An unsigned policy is disclosed where it is downloaded from, for as long as it holds - and only
  # where there is something to download. The desktop app owes nobody that sentence yet because it
  # has no public release; 4.7c is where it starts owing it, and the check turns itself on then
  # rather than being remembered.
  for c in $(jqr '.components | keys[]' "$def"); do
    [ "$(jqr ".components.\"$c\".signing.policy" "$def")" = "none" ] || continue
    local shipped
    shipped="$(count_of "$def" ".published | map(select(.component == \"$c\" and .advertised == true))")"
    if [ -z "$shipped" ] || [ "$shipped" -lt 1 ]; then
      continue
    fi
    for page in $(json_array "$def" ".components.\"$c\".signing.disclosed_in"); do
      [ -f "$root/$page" ] || { note "no page at $page to disclose the $c signing policy"; continue; }
      grep -qiF "unsigned" "$root/$page" \
        || note "$page no longer says the $c binaries are unsigned, and the policy is still none"
    done
    for workflow in $(jqr '.components[].workflow' "$def" | sort -u); do
      [ -f "$root/$workflow" ] || continue
      if grep -qE "signtool sign|codesign (-s|--sign)|notarytool submit" "$root/$workflow"; then
        note "$workflow signs a payload while the $c signing policy is none"
      fi
    done
  done

  # The advertised version has its own changelog section, matched as a whole line. `## 0.2.0` being
  # a substring of `## 0.2.0-rc.1` is exactly the confusion this contract exists to prevent.
  if [ -f "$root/CHANGELOG.md" ]; then
    grep -qxF -- "## $version" "$root/CHANGELOG.md" \
      || note "CHANGELOG.md has no '## $version' section for the advertised release"
  fi
}

# The source version may equal the advertised one - main is often exactly the published release -
# but it may never go backwards.
validate_source_version() {
  local def="$1" root="$2" prefix version
  [ -f "$root/$PROPS" ] || { note "no $PROPS"; return 1; }
  prefix="$(grep -oE "<VersionPrefix>[^<]*</VersionPrefix>" "$root/$PROPS" | sed -E "s#</?VersionPrefix>##g")"
  version="$(advertised_version "$def")"
  [ -n "$prefix" ] || { note "$PROPS declares no VersionPrefix"; return 1; }

  local lowest
  lowest="$(printf "%s\n%s\n" "$prefix" "${version%%-*}" | sort -V | head -n 1)"
  [ "$lowest" = "${version%%-*}" ] \
    || note "$PROPS declares $prefix, behind the advertised release $version"
}

# A release candidate must keep its full prerelease version everywhere, which needs the suffix to
# reach the build. release.yml passes it; app.yml did not, so every prerelease tag failed its own
# version check and no desktop candidate could be built (docs/STEPS.md V-R.0a, 4.7a).
validate_prerelease_path() {
  local def="$1" root="$2" c workflow
  for c in $(jqr '.components | keys[]' "$def"); do
    workflow="$(jqr ".components.\"$c\".workflow" "$def")"
    [ -f "$root/$workflow" ] || continue
    code_of "$root/$workflow" | grep -qF -- "-p:VersionSuffix=" \
      || note "$workflow never passes -p:VersionSuffix=, so a prerelease tag cannot build its own version"
  done

  # The changelog lookup the release runs, held to a whole-line match for the reason above.
  #
  # It follows the decision into the script (D-0109). Scoped to the line that actually reads
  # CHANGELOG.md rather than to the file, for the reason it was scoped that way before: asking
  # whether "grep -qxF" appears anywhere in release.yml passed for the wrong reason the moment the
  # publish allowlist grew one of its own, so the lookup could be weakened back and this stayed
  # green. A check that names one thing and binds another is not a check.
  local release check lookup
  release="$root/.github/workflows/release.yml"
  check="$root/scripts/require-changelog-section.sh"
  if [ -f "$release" ]; then
    if ! code_of "$release" | grep -qF "require-changelog-section.sh"; then
      note "release.yml no longer looks a version up in CHANGELOG.md at all"
    elif [ ! -f "$check" ]; then
      note "release.yml calls require-changelog-section.sh and there is no such script"
    else
      lookup="$(code_of "$check" | grep -F "CHANGELOG" | grep -F "grep " || true)"
      if [ -z "$lookup" ]; then
        note "require-changelog-section.sh no longer greps the changelog for anything"
      elif ! printf "%s" "$lookup" | grep -qF -- "-qxF"; then
        note "the changelog lookup is not a whole-line match; '## 0.2.0' would accept a '## 0.2.0-rc.1' heading"
      fi
    fi
  fi
}

# ---------------------------------------------------------------------------
# 3. published, against git history rather than against itself.
# ---------------------------------------------------------------------------
validate_history() {
  local root="$1" file="$2" revs
  # Asked of git, and a git that cannot answer FAILS rather than skips: with actions/checkout's
  # default fetch-depth of 1 the prior revisions are simply absent, and a check that treated that
  # as "nothing to compare" would pass vacuously on every runner. That is the whole defect.
  revs="$(git -C "$root" rev-list --count HEAD -- "$file" 2>/dev/null || true)"
  case "$revs" in
    "" | *[!0-9]*)
      die "cannot read the history of $file. This needs a full checkout (actions/checkout with fetch-depth: 0); an unreadable history is indistinguishable from a clean one." ;;
  esac

  if [ "$revs" -le 1 ]; then
    echo "  history: $file has no prior revision (seeding commit); nothing to compare"
    return 0
  fi

  local rev prior current v rids prior_rids
  current="$root/$file"
  for rev in $(git -C "$root" rev-list HEAD -- "$file" | tail -n +2); do
    prior="$(mktemp)"
    if ! git -C "$root" show "$rev:$file" > "$prior" 2>/dev/null; then
      rm -f "$prior"
      continue
    fi
    jq -e . "$prior" >/dev/null 2>&1 || { rm -f "$prior"; continue; }

    for v in $(jqr '.published[]?.version // empty' "$prior"); do
      if ! jq -e --arg v "$v" '[.published[].version] | index($v)' "$current" >/dev/null 2>&1; then
        note "${rev:0:8} published $v and it is no longer in $file; published releases are immutable"
        continue
      fi
      prior_rids="$(jqr --arg v "$v" '.published[] | select(.version == $v) | .rids[]' "$prior" | flatten)"
      rids="$(jqr --arg v "$v" '.published[] | select(.version == $v) | .rids[]' "$current" | flatten)"
      local prid
      for prid in $prior_rids; do
        holds "$rids" "$prid" \
          || note "${rev:0:8} recorded $v as published for $prid and $file no longer does"
      done
    done
    rm -f "$prior"
  done
}

# ---------------------------------------------------------------------------
# 4. The public origin. install.yml's mode, and nobody else's.
# ---------------------------------------------------------------------------
validate_public_origin() {
  local def="$1" control origin version c rid name url code reachable=0

  origin="$(advertised_origin "$def")"
  control="${origin}SHA256SUMS"

  # publish-release.sh's positive control, inverted. Without it a DNS failure and a deleted asset are
  # one answer, and the wrong one of the two would redden a branch that changed nothing.
  code="$(curl -sS -o /dev/null -w "%{http_code}" --max-time 20 "$control" 2>/dev/null || echo "000")"
  if [ "$code" != "200" ]; then
    echo "  origin: $control answered '$code'; the origin is not reachable from here."
    echo "  origin: reporting rather than failing - an unreachable origin is not evidence about $DEFINITION."
    return 0
  fi
  reachable=1
  echo "  origin: control $control is 200; a 404 below is now a real absence"

  local i pubs
  pubs="$(count_of "$def" '.published')"
  i=0
  while [ "$i" -lt "$pubs" ]; do
    origin="$(jqr ".published[$i].origin" "$def")"
    version="$(jqr ".published[$i].version" "$def")"
    c="$(jqr ".published[$i].component" "$def")"
    for rid in $(json_array "$def" ".published[$i].rids"); do
      name="$(archive_name "$def" "$c" "$rid" "$version")"
      url="${origin}${name}.sha256"
      code="$(curl -sS -o /dev/null -w "%{http_code}" --max-time 20 "$url" 2>/dev/null || echo "000")"
      case "$code" in
        200) echo "  origin: $version $rid present" ;;
        000) echo "  origin: $url could not be reached; not counted against the definition" ;;
        *) note "$url answered $code from a reachable origin; $DEFINITION says $version published $rid" ;;
      esac
    done
    i=$((i + 1))
  done
  [ "$reachable" = "1" ]
}

# ---------------------------------------------------------------------------
# 5. Run it: the repository first, then the fixtures that prove the checks bite.
# ---------------------------------------------------------------------------
ROOT="$(pwd)"

echo "== the definition itself"
validate_definition "$DEFINITION" || true

echo "== workflows, projects, documents"
validate_workflows "$DEFINITION" "$ROOT" || true
validate_projects "$DEFINITION" "$ROOT" || true
validate_install_jobs "$DEFINITION" "$ROOT" || true
validate_documents "$DEFINITION" "$ROOT" || true
validate_source_version "$DEFINITION" "$ROOT" || true
validate_prerelease_path "$DEFINITION" "$ROOT" || true

echo "== published, against git history"
validate_history "$ROOT" "$DEFINITION" || true

if [ "$MODE" = "--with-public-origin" ]; then
  echo "== the public origin"
  command -v curl >/dev/null 2>&1 || die "--with-public-origin needs curl and there is none"
  validate_public_origin "$DEFINITION" || true
fi

REPO_PROBLEMS=("${PROBLEMS[@]+"${PROBLEMS[@]}"}")

# ---------------------------------------------------------------------------
# 6. The fixtures. Every case is a definition that must be refused; the count is asserted, so a
#    case that silently stops being driven is not the same as a case that passes.
# ---------------------------------------------------------------------------
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

refusals=0
cases=0

# Each case names the problem it exists to provoke. "Something was refused" is not the claim - a
# fixture that trips a different check than the one it is testing leaves that check unproven while
# looking green, which is how a gate quietly stops gating.
expected_problem() {
  local want="$1" p
  for p in ${PROBLEMS[@]+"${PROBLEMS[@]}"}; do
    case "$p" in *"$want"*) return 0 ;; esac
  done
  return 1
}

report() {
  local name="$1" want="$2"
  if [ ${#PROBLEMS[@]} -eq 0 ]; then
    echo "::error::fixture '$name' was accepted and must not be"
    return 1
  fi
  if ! expected_problem "$want"; then
    echo "::error::fixture '$name' was refused, but not for '$want'. It reported:"
    local p
    for p in "${PROBLEMS[@]}"; do echo "::error::    $p"; done
    return 1
  fi
  refusals=$((refusals + 1))
  echo "  refused: $name -- $want"
}

expect_refusal() {
  local name="$1" want="$2" file="$3"
  cases=$((cases + 1))
  PROBLEMS=()
  validate_definition "$file" || true
  report "$name" "$want"
}

expect_repo_refusal() {
  local name="$1" want="$2" file="$3" root="$4"
  cases=$((cases + 1))
  PROBLEMS=()
  validate_workflows "$file" "$root" || true
  validate_projects "$file" "$root" || true
  validate_install_jobs "$file" "$root" || true
  validate_documents "$file" "$root" || true
  report "$name" "$want"
}

mutate() { jq "$2" "$DEFINITION" > "$WORK/$1.json"; echo "$WORK/$1.json"; }

echo "== fixtures: definitions that must be refused"

printf 'not json at all' > "$WORK/unparseable.json"
expect_refusal "unparseable" "is not parseable JSON" "$WORK/unparseable.json"

expect_refusal "targets-null" "cli: target count is not a number" \
  "$(mutate targets-null '.components.cli.targets = null')"
expect_refusal "targets-empty" "cli: no targets" \
  "$(mutate targets-empty '.components.cli.targets = []')"
expect_refusal "no-rid" "target 0 has no rid" \
  "$(mutate no-rid '.components.cli.targets[0] |= del(.rid)')"
expect_refusal "no-runner" "no runner" \
  "$(mutate no-runner '.components.cli.targets[0].runner = ""')"
expect_refusal "duplicate-rid" "listed twice" \
  "$(mutate duplicate-rid '.components.cli.targets[1].rid = .components.cli.targets[0].rid')"
expect_refusal "source-only-collision" "those sets must be disjoint" \
  "$(mutate source-only-collision '.components.cli.targets[0].rid = "osx-x64" | .components.cli.runtime_identifiers += ["osx-x64"]')"
expect_refusal "floor-without-evidence" "with floor_evidence none" \
  "$(mutate floor-without-evidence '.components.cli.targets[0].floor_evidence = "none"')"
expect_refusal "unverified-without-caveat" "no floor_caveat to publish" \
  "$(mutate unverified-without-caveat '.components.cli.targets[1] |= del(.floor_caveat)')"
expect_refusal "cited-without-citation" "no floor_citation to point at" \
  "$(mutate cited-without-citation '.components.cli.targets |= map(if .floor_evidence == "cited" then del(.floor_citation) else . end)')"
expect_refusal "cited-without-caveat" "no floor_caveat to publish" \
  "$(mutate cited-without-caveat '.components.cli.targets |= map(if .floor_evidence == "cited" then del(.floor_caveat) else . end)')"
expect_refusal "floor-evidence-undefined" "is not defined in docs/RELEASE.md" \
  "$(mutate floor-evidence-undefined '.components.cli.targets[0].floor_evidence = "observed"')"
expect_refusal "no-install-check-and-no-reason" "no written reason it has none" \
  "$(mutate no-install-check-and-no-reason '.components.cli.targets[1] |= (del(.install_check_absent) | .install_check = null)')"
expect_refusal "declared-not-packaged-unexplained" "no reason recorded" \
  "$(mutate declared-not-packaged-unexplained '.components.app.declared_not_packaged = {}')"
expect_refusal "published-rid-dropped" "is no longer an advertised cli target" \
  "$(mutate published-rid-dropped '.components.cli.targets |= map(select(.rid != "linux-arm64"))')"
expect_refusal "published-empty" "published is empty" \
  "$(mutate published-empty '.published = []')"
expect_refusal "nothing-advertised" "no published release is marked advertised" \
  "$(mutate nothing-advertised '.published |= map(.advertised = false)')"
expect_refusal "duplicate-published-version" "published lists 0.1.0 twice" \
  "$(mutate duplicate-published-version '.published[0].version = .published[1].version')"

echo "== fixtures: a definition the repository contradicts"

# A complete fake tree, so the only thing wrong with it is the thing being tested. A missing file
# would refuse for its own reason and prove nothing about the check above it.
FAKE="$WORK/root"
mkdir -p "$FAKE/.github/workflows" "$FAKE/site/public"
cp "$DEFINITION" "$FAKE/release-targets.json"
cp README.md CHANGELOG.md SECURITY.md "$PROPS" "$FAKE/"
mkdir -p "$FAKE/docs"
cp docs/RELEASE.md docs/desktop.md "$FAKE/docs/"
cp site/public/index.html "$FAKE/site/public/"
cp .github/workflows/release.yml .github/workflows/app.yml "$FAKE/.github/workflows/"
mkdir -p "$FAKE/scripts"
cp scripts/require-changelog-section.sh "$FAKE/scripts/"
for project in $(jqr '.components[].projects[], .shared_projects[]' "$DEFINITION"); do
  mkdir -p "$FAKE/$(dirname "$project")"
  cp "$project" "$FAKE/$project"
done

expect_repo_refusal "target-nothing-else-declares" "and the definition says" \
  "$(mutate target-nothing-else-declares '.components.cli.targets += [{"rid":"linux-musl-x64","runner":"ubuntu-22.04","archive":"tar.gz","cpu":"x64","advertised":false,"os_floor":null,"floor_evidence":"none","install_check":null,"install_check_absent":"fixture"}] | .components.cli.runtime_identifiers += ["linux-musl-x64"] | .source_only -= ["linux-musl-x64"]')" \
  "$FAKE"

expect_repo_refusal "csproj-and-definition-disagree" "and the definition says" \
  "$(mutate csproj-and-definition-disagree '.components.app.runtime_identifiers -= ["linux-arm64"] | .components.app.declared_not_packaged = {}')" \
  "$FAKE"

cp README.md "$FAKE/README.md"
printf '\nmacOS 14 or later is required.\n' >> "$FAKE/README.md"
expect_repo_refusal "doc-claims-a-floor-nothing-holds" "no target in the definition holds that floor" \
  "$FAKE/release-targets.json" "$FAKE"
cp README.md "$FAKE/README.md"

# A page that stops mentioning a floor and a page that never had one look identical to a check that
# only reads what is written. These two are why the presence rule is required rather than conditional.
sed -i '/^\*\*macOS 13 or later\.\*\*/d' "$FAKE/README.md"
expect_repo_refusal "doc-drops-a-floor" "no longer states its floor" \
  "$FAKE/release-targets.json" "$FAKE"
cp README.md "$FAKE/README.md"

sed -i 's/no run backs this floor/it has been checked/g' "$FAKE/README.md"
expect_repo_refusal "doc-drops-the-caveat" "without the caveat that qualifies it" \
  "$FAKE/release-targets.json" "$FAKE"
cp README.md "$FAKE/README.md"

# The vocabulary lives in docs/RELEASE.md and the values live in the definition. Deleting a row
# from the table is how one of them silently stops meaning anything.
sed -i '/^| `cited` |/d' "$FAKE/docs/RELEASE.md"
expect_repo_refusal "release-doc-drops-a-floor-value" "which docs/RELEASE.md does not define" \
  "$FAKE/release-targets.json" "$FAKE"
cp docs/RELEASE.md "$FAKE/docs/RELEASE.md"

sed -i 's/unsigned/perfectly ordinary/g' "$FAKE/README.md"
expect_repo_refusal "signing-disclosure-deleted" "no longer says the cli binaries are unsigned" \
  "$FAKE/release-targets.json" "$FAKE"
cp README.md "$FAKE/README.md"

# ---------------------------------------------------------------------------
# 7. NEGATIVE CONTROL. The check that stops an empty matrix reporting success, removed.
#
#    Driven against a definition stripped to nothing BUT that one question, so the weakened
#    validator has no second reason to refuse. Reusing targets-null here would prove the opposite of
#    what it looks like: its published record still names four rids the emptied component no longer
#    advertises, so both validators refuse it and the fixture would quietly stop isolating anything.
# ---------------------------------------------------------------------------
echo "== negative control"
cat > "$WORK/only-length.json" <<'ONLY'
{
  "schema": 1,
  "components": {
    "cli": {
      "binaries": [],
      "projects": [],
      "runtime_identifiers": [],
      "declared_not_packaged": {},
      "archive_pattern": "x-{version}-{rid}.{ext}",
      "origin": "https://example.invalid/v{version}/",
      "workflow": ".github/workflows/release.yml",
      "signing": { "policy": "none", "disclosed_in": [] },
      "targets": null
    }
  },
  "shared_projects": [],
  "source_only": [],
  "published": [
    { "version": "1.0.0", "tag": "v1.0.0", "component": "cli", "date": "2026-01-01",
      "origin": "https://example.invalid/v1.0.0/", "rids": [], "advertised": true }
  ]
}
ONLY

PROBLEMS=()
validate_definition "$WORK/only-length.json" || true
if [ ${#PROBLEMS[@]} -eq 0 ]; then
  die "the real validator accepted a definition with null targets; the check under test is gone"
fi
echo "  the real validator refuses null targets: ${PROBLEMS[0]}"

PROBLEMS=()
if validate_definition "$WORK/only-length.json" --weakened; then
  echo "  the weakened one accepts it, so that check is what refuses it and nothing else is"
else
  echo "::error::the weakened validator ALSO refused a definition whose only fault is null targets:"
  for problem in "${PROBLEMS[@]}"; do echo "::error::    $problem"; done
  echo "::error::The length check is then not what refuses it, and this fixture is not reproducing"
  echo "::error::the condition the gate exists to catch (D-0043)."
  exit 1
fi

# ---------------------------------------------------------------------------
# 8. The verdict.
# ---------------------------------------------------------------------------
[ "$cases" -ge 24 ] || die "only $cases fixture cases ran; cases have gone missing rather than passing"
[ "$refusals" -eq "$cases" ] || die "$refusals of $cases fixtures refused"

echo
echo "$cases fixture cases, all refused; the negative control reproduces the fail-open"

if [ ${#REPO_PROBLEMS[@]} -gt 0 ]; then
  echo
  echo "::error::the repository and $DEFINITION disagree:"
  for problem in "${REPO_PROBLEMS[@]}"; do
    echo "::error::  - $problem"
  done
  exit 1
fi

echo "ok: $DEFINITION, the workflows, the projects and the download pages hold one answer"

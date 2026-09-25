import { checkPassphrase, deriveKey, openEnvelope, parseLink } from "./share-crypto.js";

const GONE = "This link has already been opened, has expired or was revoked.";
const ALTERED = "This link could not be decrypted. It may have been altered.";
const UNREACHABLE = "keypaste.com could not be reached. No view was used; try again in a moment.";

const $ = (id) => document.getElementById(id);

// Read once and taken out of the address bar at once, so the key is not left in history or on screen.
let link = parseLink(location.hash);
history.replaceState(null, "", "/s/");

let meta = null;

function notice(text) {
  $("notice").textContent = text;
  $("notice").hidden = false;
}

function fail(text) {
  $("error").textContent = text;
  $("error").hidden = false;
}

function views(count) {
  return count === 1 ? "1 view left" : `${count} views left`;
}

function when(iso) {
  return new Date(iso).toLocaleString("en-GB", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit", hour12: false });
}

async function api(path, method) {
  const response = await fetch(path, { method, cache: "no-store", credentials: "omit", referrerPolicy: "no-referrer" });
  if (response.status === 404) return null;
  if (!response.ok) throw new Error(`status ${response.status}`);
  return response.json();
}

async function start() {
  if (!link) {
    notice("This link is incomplete. Ask for it to be sent again, copied whole.");
    return;
  }

  try {
    meta = await api(`/api/share/${link.id}`, "GET");
  } catch {
    notice(UNREACHABLE);
    return;
  }

  if (!meta) {
    link = null;
    notice(GONE);
    return;
  }

  $("notice").hidden = true;
  $("meta").textContent = `${views(meta.views_left)} · expires ${when(meta.expires_at)}`;
  $("passphrase-row").hidden = meta.kdf === null;
  $("prompt").hidden = false;
  (meta.kdf === null ? $("reveal") : $("passphrase")).focus();
}

async function reveal(event) {
  event.preventDefault();
  if (!link || !meta) return;

  $("error").hidden = true;
  $("reveal").disabled = true;

  try {
    const cek = await deriveKey(meta, link.key, $("passphrase").value);
    if (!(await checkPassphrase(meta, cek))) {
      fail(meta.kdf ? "That passphrase does not open this link. No view was used." : ALTERED);
      return;
    }

    let opened;
    try {
      opened = await api(`/api/share/${link.id}/open`, "POST");
    } catch {
      fail(UNREACHABLE);
      return;
    }

    link = null;
    $("prompt").hidden = true;
    $("passphrase").value = "";

    if (!opened) {
      notice(GONE);
      return;
    }

    let payload;
    try {
      payload = await openEnvelope(opened.envelope, cek);
    } catch {
      notice(ALTERED);
      return;
    }

    show(payload, opened.views_left);
  } finally {
    $("reveal").disabled = false;
  }
}

function show(payload, left) {
  $("heading").textContent = "The shared secret";
  $("title").textContent = payload.title;

  const list = $("fields");
  for (const field of payload.fields) {
    const row = document.createElement("div");
    row.className = "secret";

    const name = document.createElement("p");
    name.className = "name";
    name.textContent = field.name;

    const value = document.createElement("code");
    value.className = "value";
    value.textContent = field.value;

    const copy = document.createElement("button");
    copy.type = "button";
    copy.className = "copy";
    copy.textContent = "Copy";
    copy.setAttribute("aria-label", `Copy ${field.name}`);
    copy.addEventListener("click", async () => {
      try {
        await navigator.clipboard.writeText(field.value);
        copy.textContent = "Copied";
      } catch {
        copy.textContent = "Select and copy";
      }
    });

    row.append(name, value, copy);
    list.append(row);
  }

  $("after").textContent = left === 0 ? "That was the last view: the link no longer opens." : `${views(left)}.`;
  $("result").hidden = false;
}

$("prompt").addEventListener("submit", reveal);
start();

// The browser half of a share link. src/Keypaste.Core/Sharing/ShareCrypto.cs seals what this opens,
// and site/test/share-vector.json holds both to the same bytes (D-0354).

const encoder = new TextEncoder();
const decoder = new TextDecoder("utf-8", { fatal: true });

const ID_CHARS = 22;
const KEY_CHARS = 43;
const B64URL = /^[A-Za-z0-9_-]+$/;

export function base64urlToBytes(text) {
  if (typeof text !== "string" || !B64URL.test(text)) throw new Error("not base64url");
  const base64 = text.replace(/-/g, "+").replace(/_/g, "/");
  const binary = atob(base64 + "=".repeat((4 - (base64.length % 4)) % 4));
  return Uint8Array.from(binary, (c) => c.charCodeAt(0));
}

// "#<id>.<key>", or null when the fragment is not a whole link.
export function parseLink(hash) {
  const fragment = (hash ?? "").replace(/^#/, "");
  const dot = fragment.indexOf(".");
  if (dot < 0) return null;

  const id = fragment.slice(0, dot);
  const key = fragment.slice(dot + 1);
  if (id.length !== ID_CHARS || key.length !== KEY_CHARS || !B64URL.test(id) || !B64URL.test(key)) return null;

  return { id, key: base64urlToBytes(key) };
}

function aad(kdf) {
  return encoder.encode(
    kdf ? `keypaste-share:v1:pbkdf2-sha256:${kdf.iterations}:${kdf.salt}` : "keypaste-share:v1:none",
  );
}

async function hkdf(ikm, info) {
  const base = await crypto.subtle.importKey("raw", ikm, "HKDF", false, ["deriveKey"]);
  return crypto.subtle.deriveKey(
    { name: "HKDF", hash: "SHA-256", salt: new Uint8Array(0), info: encoder.encode(info) },
    base,
    { name: "AES-GCM", length: 256 },
    false,
    ["decrypt"],
  );
}

// The content key: HKDF of the link key, or of the link key and the passphrase's PBKDF2 output.
export async function deriveKey(envelope, keyBytes, passphrase) {
  if (!envelope.kdf) return hkdf(keyBytes, "keypaste share v1");

  const password = await crypto.subtle.importKey(
    "raw",
    encoder.encode((passphrase ?? "").normalize("NFC")),
    "PBKDF2",
    false,
    ["deriveBits"],
  );
  const stretched = new Uint8Array(
    await crypto.subtle.deriveBits(
      {
        name: "PBKDF2",
        hash: "SHA-256",
        salt: base64urlToBytes(envelope.kdf.salt),
        iterations: envelope.kdf.iterations,
      },
      password,
      256,
    ),
  );

  const material = new Uint8Array(keyBytes.length + stretched.length);
  material.set(keyBytes);
  material.set(stretched, keyBytes.length);
  return hkdf(material, "keypaste share v1 passphrase");
}

// Whether the content key opens the check tag, which needs only the metadata and spends no view.
export async function checkPassphrase(envelopeMeta, cek) {
  try {
    await crypto.subtle.decrypt(
      {
        name: "AES-GCM",
        iv: base64urlToBytes(envelopeMeta.check_iv),
        additionalData: encoder.encode("keypaste-share-check:v1"),
        tagLength: 128,
      },
      cek,
      base64urlToBytes(envelopeMeta.check),
    );
    return true;
  } catch {
    return false;
  }
}

// The payload: { v, title, fields: [{ name, value }], created }. Throws when it does not open.
export async function openEnvelope(envelope, cek) {
  const plaintext = await crypto.subtle.decrypt(
    { name: "AES-GCM", iv: base64urlToBytes(envelope.iv), additionalData: aad(envelope.kdf), tagLength: 128 },
    cek,
    base64urlToBytes(envelope.ct),
  );
  const payload = JSON.parse(decoder.decode(plaintext));
  if (payload?.v !== 1 || typeof payload.title !== "string" || !Array.isArray(payload.fields)) {
    throw new Error("not a keypaste share");
  }
  return payload;
}

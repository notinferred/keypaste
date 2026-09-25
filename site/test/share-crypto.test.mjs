import { test } from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

import {
  base64urlToBytes,
  checkPassphrase,
  deriveKey,
  openEnvelope,
  parseLink,
} from "../public/s/share-crypto.js";

const vector = JSON.parse(await readFile(new URL("./share-vector.json", import.meta.url), "utf8"));

function flipFirstChar(text) {
  return (text[0] === "A" ? "B" : "A") + text.slice(1);
}

async function open(envelope, key, passphrase) {
  const cek = await deriveKey(envelope, base64urlToBytes(key), passphrase);
  return openEnvelope(envelope, cek);
}

for (const sample of vector.cases) {
  const envelope = JSON.parse(sample.envelope);

  test(`${sample.name}: the vector C# sealed opens to its payload`, async () => {
    const payload = await open(envelope, sample.key, sample.passphrase);

    assert.equal(payload.v, 1);
    assert.equal(payload.title, sample.payload.title);
    assert.deepEqual(payload.fields, sample.payload.fields);
    assert.equal(payload.created, sample.payload.created);
  });

  test(`${sample.name}: the check passes with the right key and passphrase`, async () => {
    const cek = await deriveKey(envelope, base64urlToBytes(sample.key), sample.passphrase);
    const { iv, ct, ...meta } = envelope;

    assert.equal(await checkPassphrase(meta, cek), true);
  });

  test(`${sample.name}: altered ciphertext does not open`, async () => {
    await assert.rejects(open({ ...envelope, ct: flipFirstChar(envelope.ct) }, sample.key, sample.passphrase));
  });

  test(`${sample.name}: another key does not open it`, async () => {
    await assert.rejects(open(envelope, flipFirstChar(sample.key), sample.passphrase));
  });
}

const withPassphrase = vector.cases.find((sample) => sample.passphrase !== "");
const passphraseEnvelope = JSON.parse(withPassphrase.envelope);

test("lowered iterations do not open it", async () => {
  const lowered = { ...passphraseEnvelope, kdf: { ...passphraseEnvelope.kdf, iterations: 999 } };

  await assert.rejects(open(lowered, withPassphrase.key, withPassphrase.passphrase));
});

test("a swapped salt does not open it", async () => {
  const swapped = { ...passphraseEnvelope, kdf: { ...passphraseEnvelope.kdf, salt: flipFirstChar(passphraseEnvelope.kdf.salt) } };

  await assert.rejects(open(swapped, withPassphrase.key, withPassphrase.passphrase));
});

test("a wrong passphrase fails the check without the ciphertext", async () => {
  const cek = await deriveKey(passphraseEnvelope, base64urlToBytes(withPassphrase.key), "not the passphrase");
  const { iv, ct, ...meta } = passphraseEnvelope;

  assert.equal(await checkPassphrase(meta, cek), false);
});

test("parseLink reads the id and key and refuses anything else", () => {
  const id = "AAAAAAAAAAAAAAAAAAAAAA";
  const key = vector.cases[0].key;

  const parsed = parseLink(`#${id}.${key}`);
  assert.equal(parsed.id, id);
  assert.deepEqual(parsed.key, base64urlToBytes(key));

  assert.equal(parseLink(""), null);
  assert.equal(parseLink(`#${id}`), null);
  assert.equal(parseLink(`#${id}.${key.slice(1)}`), null);
  assert.equal(parseLink(`#${id}.${key}=`), null);
  assert.equal(parseLink(`#${id.slice(1)}+.${key}`), null);
});

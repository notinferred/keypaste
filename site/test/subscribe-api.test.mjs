import { test } from "node:test";
import assert from "node:assert/strict";

import worker from "../src/worker.js";

const SENDER = "203.0.113.7";
const OTHER_SENDER = "203.0.113.8";
const ctx = { waitUntil() {} };

// The honeypot answers 303 before any database connection, so these tests reach no database.
const HONEYPOT = "website=a-bot-filled-this&email=honeypot@example.com";

function tenPerAddress() {
  const counts = new Map();
  return {
    async limit({ key }) {
      counts.set(key, (counts.get(key) ?? 0) + 1);
      return { success: counts.get(key) <= 10 };
    },
  };
}

function post(env, body, sender = SENDER) {
  const request = new Request("https://keypaste.com/subscribe", {
    method: "POST",
    headers: { "content-type": "application/x-www-form-urlencoded", "cf-connecting-ip": sender },
    body,
  });
  return worker.fetch(request, env, ctx);
}

test("one address gets ten submissions a minute, and another address keeps its own ten", async () => {
  const env = { SUBSCRIBE_LIMIT: tenPerAddress() };

  for (let i = 0; i < 10; i++) assert.equal((await post(env, HONEYPOT)).status, 303);

  assert.equal((await post(env, HONEYPOT)).status, 429);
  assert.equal((await post(env, HONEYPOT, OTHER_SENDER)).status, 303);
});

test("a limited signup opens no database and is told its address was not stored", async () => {
  let databaseOpened = false;
  const env = {
    SUBSCRIBE_LIMIT: { limit: async () => ({ success: false }) },
    get HYPERDRIVE() {
      databaseOpened = true;
      throw new Error("the limit should have answered first");
    },
  };

  const response = await post(env, "email=person@example.com");
  const page = await response.text();

  assert.equal(response.status, 429);
  assert.equal(databaseOpened, false);
  assert.match(response.headers.get("content-type"), /^text\/html/);
  assert.match(page, /your address was not stored/);
  assert.doesNotMatch(page, /database/i);
  assert.doesNotMatch(page, /<script/i);
});

test("a missing binding means no limit", async () => {
  for (let i = 0; i < 11; i++) assert.equal((await post({}, HONEYPOT)).status, 303);
});

#!/usr/bin/env node
'use strict';

// Prints each test public key as a Chromium manifest `key` and the extension ID it gives: the first 32 hex digits of
// the SHA-256 of its DER SubjectPublicKeyInfo, each digit 0-f written as a-p.

const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');

for (const name of fs.readdirSync(__dirname).filter((file) => file.endsWith('.pub')).sort()) {
    const der = crypto.createPublicKey(fs.readFileSync(path.join(__dirname, name))).export({ type: 'spki', format: 'der' });
    const id = [ ...crypto.createHash('sha256').update(der).digest('hex').slice(0, 32) ]
        .map((digit) => String.fromCharCode('a'.charCodeAt(0) + parseInt(digit, 16)))
        .join('');
    console.log(`${path.basename(name, '.pub')} ${id} ${der.toString('base64')}`);
}

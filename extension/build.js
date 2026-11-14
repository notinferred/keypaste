#!/usr/bin/env node
'use strict';

// Builds artifacts/extension/keypaste-<browser>.zip and an unpacked copy of each, byte for byte the same on every run:
// files in sorted order, every timestamp 1980-01-01 00:00 and every mode 0644.

const fs = require('node:fs');
const path = require('node:path');
const zlib = require('node:zlib');

const SOURCE = path.join(__dirname, 'src');
const LICENSE = path.join(__dirname, 'LICENSE');
const OUTPUT = path.join(__dirname, '..', 'artifacts', 'extension');
const MANIFESTS = {
    chromium: path.join(__dirname, 'dist', 'manifest_chromium.json'),
    firefox: path.join(__dirname, 'dist', 'manifest_firefox.json'),
};
// Playwright's global setup copies the tests into src/tests while they run
const EXCLUDED = new Set([ 'tests' ]);

const DOS_TIME = 0;
const DOS_DATE = (1 << 5) | 1;
const UNIX_FILE_MODE = 0o100644;

const sourceFiles = function(directory, prefix = '') {
    const files = [];
    for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
        if (entry.name.startsWith('.') || (prefix === '' && EXCLUDED.has(entry.name))) {
            continue;
        }

        const relative = prefix + entry.name;
        const absolute = path.join(directory, entry.name);
        if (entry.isDirectory()) {
            files.push(...sourceFiles(absolute, relative + '/'));
        } else if (entry.isFile()) {
            files.push(relative);
        }
    }

    return files;
};

const CRC_TABLE = Array.from({ length: 256 }, (_, n) => {
    let c = n;
    for (let k = 0; k < 8; k++) {
        c = (c & 1) ? (0xEDB88320 ^ (c >>> 1)) : (c >>> 1);
    }
    return c >>> 0;
});

const crc32 = function(data) {
    let crc = 0xFFFFFFFF;
    for (const byte of data) {
        crc = CRC_TABLE[(crc ^ byte) & 0xFF] ^ (crc >>> 8);
    }
    return (crc ^ 0xFFFFFFFF) >>> 0;
};

const zip = function(entries) {
    const chunks = [];
    const directory = [];
    let offset = 0;

    for (const { name, data } of entries) {
        const fileName = Buffer.from(name, 'utf8');
        const deflated = zlib.deflateRawSync(data, { level: 9 });
        const stored = deflated.length >= data.length;
        const body = stored ? data : deflated;
        const method = stored ? 0 : 8;
        const crc = crc32(data);

        const local = Buffer.alloc(30);
        local.writeUInt32LE(0x04034B50, 0);
        local.writeUInt16LE(20, 4);
        local.writeUInt16LE(0, 6);
        local.writeUInt16LE(method, 8);
        local.writeUInt16LE(DOS_TIME, 10);
        local.writeUInt16LE(DOS_DATE, 12);
        local.writeUInt32LE(crc, 14);
        local.writeUInt32LE(body.length, 18);
        local.writeUInt32LE(data.length, 22);
        local.writeUInt16LE(fileName.length, 26);
        local.writeUInt16LE(0, 28);

        const central = Buffer.alloc(46);
        central.writeUInt32LE(0x02014B50, 0);
        central.writeUInt16LE((3 << 8) | 20, 4);
        central.writeUInt16LE(20, 6);
        central.writeUInt16LE(0, 8);
        central.writeUInt16LE(method, 10);
        central.writeUInt16LE(DOS_TIME, 12);
        central.writeUInt16LE(DOS_DATE, 14);
        central.writeUInt32LE(crc, 16);
        central.writeUInt32LE(body.length, 20);
        central.writeUInt32LE(data.length, 24);
        central.writeUInt16LE(fileName.length, 28);
        central.writeUInt16LE(0, 30);
        central.writeUInt16LE(0, 32);
        central.writeUInt16LE(0, 34);
        central.writeUInt16LE(0, 36);
        central.writeUInt32LE((UNIX_FILE_MODE << 16) >>> 0, 38);
        central.writeUInt32LE(offset, 42);

        chunks.push(local, fileName, body);
        directory.push(central, fileName);
        offset += local.length + fileName.length + body.length;
    }

    const directoryBytes = Buffer.concat(directory);
    const end = Buffer.alloc(22);
    end.writeUInt32LE(0x06054B50, 0);
    end.writeUInt16LE(entries.length, 8);
    end.writeUInt16LE(entries.length, 10);
    end.writeUInt32LE(directoryBytes.length, 12);
    end.writeUInt32LE(offset, 16);

    return Buffer.concat([ ...chunks, directoryBytes, end ]);
};

const build = function(browser, manifestPath, files) {
    const entries = files
        .map((name) => ({ name, data: fs.readFileSync(path.join(SOURCE, name)) }))
        .concat({ name: 'LICENSE', data: fs.readFileSync(LICENSE) }, { name: 'manifest.json', data: fs.readFileSync(manifestPath) })
        .sort((a, b) => Buffer.compare(Buffer.from(a.name), Buffer.from(b.name)));

    const unpacked = path.join(OUTPUT, browser);
    fs.rmSync(unpacked, { recursive: true, force: true });
    for (const { name, data } of entries) {
        const target = path.join(unpacked, name);
        fs.mkdirSync(path.dirname(target), { recursive: true });
        fs.writeFileSync(target, data, { mode: 0o644 });
    }

    const archive = path.join(OUTPUT, `keypaste-${browser}.zip`);
    fs.writeFileSync(archive, zip(entries));
    console.log(`${path.relative(process.cwd(), archive)}: ${entries.length} files`);
};

const files = sourceFiles(SOURCE);
fs.mkdirSync(OUTPUT, { recursive: true });
for (const [ browser, manifestPath ] of Object.entries(MANIFESTS)) {
    build(browser, manifestPath, files);
}

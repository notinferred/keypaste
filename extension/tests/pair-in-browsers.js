#!/usr/bin/env node
'use strict';

// scripts/verify-extension-load.sh's browser half: tests/Keypaste.AppDriver holds the vault, turns the host on for Chromium and
// Firefox, and arms pairing; each browser loads its unpacked build, its options page presses Connect, the driver allows the
// pairing, and the page and Settings' list must both show it under that build.
//
// Usage: pair-in-browsers.js <root> <driver> <keypaste> <vault> <chromium build> <firefox build> <firefox> <geckodriver> <chrome id>

const { spawn } = require('node:child_process');
const crypto = require('node:crypto');
const net = require('node:net');
const path = require('node:path');
const readline = require('node:readline');
const { chromium } = require('@playwright/test');

const WAIT_MS = 60 * 1000;
const FIREFOX_ID = 'browser@keypaste.invalid';
const ELEMENT = 'element-6066-11e4-a52e-4f735466cecf';

const [ root, driverPath, keypaste, vault, chromiumBuild, firefoxBuild, firefoxBinary, geckodriverPath, chromeId ] = process.argv.slice(2);
if (!chromeId) {
    console.error('usage: pair-in-browsers.js <root> <driver> <keypaste> <vault> <chromium build> <firefox build> <firefox> <geckodriver> <chrome id>');
    process.exit(2);
}

const home = path.join(root, 'home');
const configHome = path.join(root, 'config');
const keypasteHome = path.join(root, 'keypaste-home');
const { KEYPASTE_DRIVER_PASSWORD: password, ...inherited } = process.env;
// What each browser passes to the keypaste it starts: the user folders the host was set up in, and the held vault.
const browserEnvironment = { ...inherited, HOME: home, XDG_CONFIG_HOME: configHome, KEYPASTE_HOME: keypasteHome, KEYPASTE_VAULT: vault };

const escape = (text) => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

const withinWait = function(promise, what) {
    let timer;
    const timeout = new Promise((_, reject) => {
        timer = setTimeout(() => reject(new Error(`timed out after ${WAIT_MS / 1000}s waiting for ${what}`)), WAIT_MS);
    });
    return Promise.race([ promise, timeout ]).finally(() => clearTimeout(timer));
};

class HeldApp {
    constructor() {
        this.lines = [];
        this.waiters = [];
        this.process = spawn(driverPath, [ 'hold', vault ], {
            env: {
                ...inherited,
                KEYPASTE_HOME: keypasteHome,
                KEYPASTE_DRIVER_PASSWORD: password,
                KEYPASTE_DRIVER_BROWSER_PROGRAM: keypaste,
                KEYPASTE_DRIVER_BROWSER_HOME: home,
                KEYPASTE_DRIVER_BROWSER_USERPROFILE: home,
                KEYPASTE_DRIVER_BROWSER_XDG_CONFIG_HOME: configHome,
                KEYPASTE_DRIVER_BROWSER_APPDATA: path.join(root, 'appdata'),
                KEYPASTE_DRIVER_BROWSER_LOCALAPPDATA: path.join(root, 'localappdata'),
                KEYPASTE_DRIVER_BROWSER_REGISTRY_ROOT: 'Software\\keypaste-tests\\extension-load',
            },
            stdio: [ 'pipe', 'pipe', 'inherit' ],
        });
        readline.createInterface({ input: this.process.stdout }).on('line', (line) => {
            console.log(`driver: ${line}`);
            this.lines.push(line);
            this.waiters = this.waiters.filter((waiter) => !waiter(line));
        });
        this.exited = new Promise((resolve) => this.process.on('exit', resolve));
    }

    send(command) {
        console.log(`driver< ${command}`);
        this.process.stdin.write(command + '\n');
    }

    // The next line from now on matching pattern.
    next(pattern) {
        return withinWait(new Promise((resolve) => {
            this.waiters.push((line) => {
                if (!pattern.test(line)) {
                    return false;
                }
                resolve(line);
                return true;
            });
        }), `the driver to print ${pattern}`);
    }

    async act(command, pattern) {
        const answer = this.next(pattern);
        this.send(command);
        return answer;
    }

    async quit() {
        this.process.stdin.end();
        await withinWait(this.exited, 'the driver to shut down');
    }
}

// Arms pairing, presses Connect through press(), allows the prompt the driver shows and returns the pairing's name.
const pair = async function(app, build, press) {
    await app.act('pair', /^pairing (armed|not armed)/).then((line) => {
        if (line !== 'pairing armed') {
            throw new Error(line);
        }
    });
    const prompt = app.next(/^pair-prompt identity=/);
    await press();
    const shown = await prompt;
    const match = /^pair-prompt identity=(.*) name=(.*)$/.exec(shown);
    if (!match || match[1] !== build) {
        throw new Error(`the prompt names ${match ? match[1] : shown}, not ${build}`);
    }
    await app.act('allow', /^pair-prompt withdrawn/);
    return match[2];
};

const requirePaired = async function(app, build, name, shown) {
    const expected = `This browser is paired with keypaste as ${name}.`;
    if (shown !== expected) {
        throw new Error(`the options page says '${shown}', not '${expected}'`);
    }
    const listed = await app.act('pairings', new RegExp(`^pairing identity=${escape(build)} name=${escape(name)}$|^pairings none$`));
    if (listed === 'pairings none') {
        throw new Error(`Settings lists no pairing for ${build}`);
    }
    console.log(`paired ${build} as ${name}`);
};

const pairChromium = async function(app) {
    const build = `keypaste's extension from the Chrome Web Store (${chromeId})`;
    // Chromium on Linux reads a user's host manifests from <user data dir>/NativeMessagingHosts, where the driver set Chromium up.
    const context = await chromium.launchPersistentContext(path.join(configHome, 'chromium'), {
        channel: 'chromium',
        headless: true,
        env: browserEnvironment,
        ignoreDefaultArgs: [ '--disable-extensions' ],
        args: [ `--disable-extensions-except=${chromiumBuild}`, `--load-extension=${chromiumBuild}` ],
    });
    try {
        const worker = context.serviceWorkers()[0] ?? await context.waitForEvent('serviceworker', { timeout: WAIT_MS });
        const id = new URL(worker.url()).host;
        if (id !== chromeId) {
            throw new Error(`Chromium loaded the extension as ${id}, not ${chromeId}`);
        }

        const page = await context.newPage();
        await page.goto(`chrome-extension://${id}/options/options.html`);
        // The page shows both lines until it has read the pairing.
        await page.locator('#paired').waitFor({ state: 'hidden', timeout: WAIT_MS });
        const name = await pair(app, build, () => page.locator('#connect-button').click());
        await page.locator('#paired').waitFor({ state: 'visible', timeout: WAIT_MS });
        await requirePaired(app, build, name, (await page.locator('#paired').textContent()).trim());
    } finally {
        await context.close();
    }
};

const freePort = () => new Promise((resolve, reject) => {
    const server = net.createServer();
    server.on('error', reject);
    server.listen(0, '127.0.0.1', () => {
        const { port } = server.address();
        server.close(() => resolve(port));
    });
});

// The W3C WebDriver commands geckodriver answers, plus its own add-on install.
class Gecko {
    static async start() {
        const port = await freePort();
        const gecko = new Gecko(port, spawn(geckodriverPath, [ '--port', String(port), '--log', 'warn' ], { env: browserEnvironment, stdio: 'inherit' }));
        await withinWait((async () => {
            for (;;) {
                try {
                    const status = await fetch(`${gecko.base}/status`);
                    if (status.ok) {
                        return;
                    }
                } catch {
                    // Not listening yet.
                }
                await new Promise((resolve) => setTimeout(resolve, 200));
            }
        })(), 'geckodriver to listen');
        return gecko;
    }

    constructor(port, child) {
        this.base = `http://127.0.0.1:${port}`;
        this.child = child;
        this.exited = new Promise((resolve) => child.on('exit', resolve));
    }

    async call(method, route, body) {
        const response = await fetch(this.base + route, {
            method,
            headers: { 'Content-Type': 'application/json' },
            body: body === undefined ? undefined : JSON.stringify(body),
        });
        const answer = await response.json();
        if (!response.ok) {
            throw new Error(`${method} ${route}: ${JSON.stringify(answer.value)}`);
        }
        return answer.value;
    }

    session(method, route, body) {
        return this.call(method, `/session/${this.id}${route}`, body);
    }

    async open(uuid) {
        const session = await this.call('POST', '/session', {
            capabilities: {
                alwaysMatch: {
                    browserName: 'firefox',
                    'moz:firefoxOptions': {
                        binary: firefoxBinary,
                        args: [ '-headless' ],
                        // Fixes the add-on's internal UUID, which names its pages' moz-extension:// origin.
                        prefs: { 'extensions.webextensions.uuids': JSON.stringify({ [FIREFOX_ID]: uuid }) },
                    },
                },
            },
        });
        this.id = session.sessionId;
    }

    async element(selector) {
        return (await this.session('POST', '/element', { using: 'css selector', value: selector }))[ELEMENT];
    }

    async waitDisplayed(selector, displayed = true) {
        return withinWait((async () => {
            for (;;) {
                const element = await this.element(selector);
                if (await this.session('GET', `/element/${element}/displayed`) === displayed) {
                    return element;
                }
                await new Promise((resolve) => setTimeout(resolve, 200));
            }
        })(), `${selector} to ${displayed ? 'show' : 'hide'}`);
    }

    async stop() {
        if (this.id) {
            await this.session('DELETE', '').catch((error) => console.error(`closing Firefox: ${error.message}`));
        }
        this.child.kill();
        await withinWait(this.exited, 'geckodriver to exit');
    }
}

const pairFirefox = async function(app) {
    const build = `keypaste's extension from Firefox Add-ons (${FIREFOX_ID})`;
    const uuid = crypto.randomUUID();
    // Firefox on Linux reads a user's host manifests from ~/.mozilla/native-messaging-hosts, under the HOME the driver set Firefox up in.
    const gecko = await Gecko.start();
    try {
        await gecko.open(uuid);
        const installed = await gecko.session('POST', '/moz/addon/install', { path: firefoxBuild, temporary: true });
        if (installed !== FIREFOX_ID) {
            throw new Error(`Firefox installed the add-on as ${installed}, not ${FIREFOX_ID}`);
        }

        await gecko.session('POST', '/url', { url: `moz-extension://${uuid}/options/options.html` });
        await gecko.waitDisplayed('#paired', false);
        const connect = await gecko.element('#connect-button');
        const name = await pair(app, build, () => gecko.session('POST', `/element/${connect}/click`, {}));
        const paired = await gecko.waitDisplayed('#paired');
        await requirePaired(app, build, name, (await gecko.session('GET', `/element/${paired}/text`)).trim());
    } finally {
        await gecko.stop();
    }
};

(async () => {
    const app = new HeldApp();
    let code = 0;
    try {
        const held = await app.next(/^holding session /);
        if (held.includes('not served')) {
            throw new Error(held);
        }
        for (const browser of [ 'chromium', 'firefox' ]) {
            const said = await app.act(`browser-on ${browser}`, new RegExp(`^browser-on ${browser}`));
            if (!said.startsWith(`browser-on ${browser}: `)) {
                throw new Error(said);
            }
        }

        await pairChromium(app);
        await pairFirefox(app);
    } catch (error) {
        console.error(`::error::${error.message}`);
        code = 1;
    } finally {
        await app.quit().catch((error) => {
            console.error(`::error::${error.message}`);
            app.process.kill('SIGKILL');
            code = 1;
        });
    }
    process.exit(code);
})();

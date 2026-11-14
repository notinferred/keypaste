#!/usr/bin/env node
'use strict';

// scripts/verify-extension-load.sh's browser half: tests/Keypaste.AppDriver holds the vault, turns the host on for Chromium and
// Firefox, and arms pairing; each browser loads its unpacked build, its options page presses Connect once the extension has
// connected, the driver allows the pairing, and the page and Settings' list must both show it under that build. On a failure
// it prints what each browser and keypaste left: the extension's status, page and console, the host manifest and the logs.
//
// Usage: pair-in-browsers.js <root> <driver> <keypaste> <vault> <chromium build> <firefox build> <firefox> <geckodriver> <chrome id>

const { spawn } = require('node:child_process');
const crypto = require('node:crypto');
const fs = require('node:fs');
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

const pause = () => new Promise((resolve) => setTimeout(resolve, 200));

const readOr = function(file) {
    try {
        return fs.readFileSync(file, 'utf8');
    } catch (error) {
        return `(${error.message})`;
    }
};

const listOr = function(directory) {
    try {
        return fs.readdirSync(directory).join('\n');
    } catch (error) {
        return `(${error.message})`;
    }
};

// What a failed pairing leaves to read, each part in its own collapsed group of the job's log.
const report = function(sections) {
    for (const [ title, text ] of sections) {
        console.error(`::group::${title}`);
        console.error(text || '(empty)');
        console.error('::endgroup::');
    }
};

const hostSections = (manifestDirectory) => [
    [ `host manifests in ${manifestDirectory}`, listOr(manifestDirectory) ],
    [ 'com.keypaste.browser.json', readOr(path.join(manifestDirectory, 'com.keypaste.browser.json')) ],
    [ 'the owner\'s audit', readOr(path.join(keypasteHome, 'audit.jsonl')) ],
];

// The extension connects to keypaste as it starts, and until it has, Connect gives up without asking keypaste anything.
const waitConnected = function(status, seen) {
    return withinWait((async () => {
        for (;;) {
            seen.status = await status();
            if (seen.status?.keypasteAvailable && !seen.status.databaseClosed) {
                return;
            }
            await pause();
        }
    })(), 'the extension to connect to keypaste');
};

// The background's own answer to get_status, without asking keypaste again.
const STATUS_ACTION = { action: 'get_status', args: [ true ] };

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

// Chromium started here rather than by Playwright, so its standard error, where the hosts it starts write theirs, reaches a file.
const launchChromium = async function(log) {
    const child = spawn(chromium.executablePath(), [
        '--headless',
        '--no-sandbox',
        '--no-first-run',
        '--no-default-browser-check',
        '--disable-background-networking',
        '--disable-component-update',
        '--password-store=basic',
        '--use-mock-keychain',
        '--enable-logging=stderr',
        '--v=0',
        '--remote-debugging-port=0',
        // Chromium on Linux reads a user's host manifests from <user data dir>/NativeMessagingHosts, where the driver set Chromium up.
        `--user-data-dir=${path.join(configHome, 'chromium')}`,
        `--disable-extensions-except=${chromiumBuild}`,
        `--load-extension=${chromiumBuild}`,
        'about:blank',
    ], { env: browserEnvironment, stdio: [ 'ignore', 'pipe', 'pipe' ] });
    const exited = new Promise((resolve) => child.on('exit', resolve));
    const out = fs.createWriteStream(log);
    child.stdout.pipe(out, { end: false });
    const stop = async () => {
        child.kill();
        await withinWait(exited, 'Chromium to exit');
        await new Promise((resolve) => out.end(resolve));
    };
    try {
        const endpoint = await withinWait(new Promise((resolve, reject) => {
            readline.createInterface({ input: child.stderr }).on('line', (line) => {
                out.write(line + '\n');
                const listening = /DevTools listening on (ws:\/\/\S+)/.exec(line);
                if (listening) {
                    resolve(listening[1]);
                }
            });
            exited.then((code) => reject(new Error(`Chromium exited with ${code} before it listened for DevTools`)));
        }), 'Chromium to listen for DevTools');
        const browser = await chromium.connectOverCDP(endpoint);
        return { context: browser.contexts()[0], stop: async () => {
            await browser.close();
            await stop();
        } };
    } catch (error) {
        await stop();
        throw error;
    }
};

const pairChromium = async function(app) {
    const build = `keypaste's extension from the Chrome Web Store (${chromeId})`;
    const log = path.join(root, 'chromium.log');
    const consoles = [];
    const seen = {};
    let page;
    let stop = async () => {};
    try {
        const launched = await launchChromium(log);
        stop = launched.stop;
        const { context } = launched;
        const listen = (worker) => worker.on('console', (message) => consoles.push(`worker ${message.type()}: ${message.text()}`));
        context.on('serviceworker', listen);
        context.serviceWorkers().forEach(listen);
        // Chromium runs component extensions of its own beside keypaste's, so the worker is found by the ID the test key gives.
        const ours = (worker) => new URL(worker.url()).host === chromeId;
        const worker = context.serviceWorkers().find(ours)
            ?? await context.waitForEvent('serviceworker', { predicate: ours, timeout: WAIT_MS }).catch(() => {
                const seen = context.serviceWorkers().map((other) => other.url()).join(', ') || 'none';
                throw new Error(`Chromium ran no extension as ${chromeId}; its extension workers: ${seen}`);
            });
        const id = new URL(worker.url()).host;

        page = await context.newPage();
        page.on('console', (message) => consoles.push(`page ${message.type()}: ${message.text()}`));
        page.on('pageerror', (error) => consoles.push(`page error: ${error.message}`));
        await page.goto(`chrome-extension://${id}/options/options.html`);
        // The page shows both lines until it has read the pairing.
        await page.locator('#paired').waitFor({ state: 'hidden', timeout: WAIT_MS });
        await waitConnected(() => page.evaluate((message) => browser.runtime.sendMessage(message), STATUS_ACTION), seen);
        const name = await pair(app, build, () => page.locator('#connect-button').click());
        await page.locator('#paired').waitFor({ state: 'visible', timeout: WAIT_MS });
        await requirePaired(app, build, name, (await page.locator('#paired').textContent()).trim());
    } catch (error) {
        const shown = page ? await page.locator('body').innerText().catch((reason) => `(${reason.message})`) : '(no page)';
        await stop().catch((reason) => console.error(`closing Chromium: ${reason.message}`));
        report([
            [ 'Chromium: the extension\'s last status', JSON.stringify(seen.status) ],
            [ 'Chromium: the options page', shown ],
            [ 'Chromium: the extension\'s console', consoles.join('\n') ],
            ...hostSections(path.join(configHome, 'chromium', 'NativeMessagingHosts')),
            [ `Chromium's log, with keypaste's standard error (${log})`, readOr(log) ],
        ]);
        throw error;
    }
    await stop();
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
    // geckodriver's log, with Firefox's standard output and error, where the hosts it starts write theirs, goes to log.
    static async start(log) {
        const port = await freePort();
        const output = fs.openSync(log, 'w');
        const child = spawn(geckodriverPath, [ '--port', String(port), '--log', 'info' ], { env: browserEnvironment, stdio: [ 'ignore', output, output ] });
        fs.closeSync(output);
        const gecko = new Gecko(port, child);
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
                        prefs: {
                            'extensions.webextensions.uuids': JSON.stringify({ [FIREFOX_ID]: uuid }),
                            // The add-on's console, into Firefox's standard output.
                            'devtools.console.stdout.chrome': true,
                            'devtools.console.stdout.content': true,
                        },
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

    // The options page's own browser.runtime.sendMessage.
    async ask(message) {
        return this.session('POST', '/execute/async', {
            script: 'const done = arguments[1]; browser.runtime.sendMessage(arguments[0]).then(done, (error) => done({ failed: String(error) }));',
            args: [ message ],
        });
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
    const log = path.join(root, 'geckodriver.log');
    const seen = {};
    let gecko;
    try {
        gecko = await Gecko.start(log);
        await gecko.open(uuid);
        const installed = await gecko.session('POST', '/moz/addon/install', { path: firefoxBuild, temporary: true });
        if (installed !== FIREFOX_ID) {
            throw new Error(`Firefox installed the add-on as ${installed}, not ${FIREFOX_ID}`);
        }

        await gecko.session('POST', '/url', { url: `moz-extension://${uuid}/options/options.html` });
        await gecko.waitDisplayed('#paired', false);
        await waitConnected(() => gecko.ask(STATUS_ACTION), seen);
        const connect = await gecko.element('#connect-button');
        const name = await pair(app, build, () => gecko.session('POST', `/element/${connect}/click`, {}));
        const paired = await gecko.waitDisplayed('#paired');
        await requirePaired(app, build, name, (await gecko.session('GET', `/element/${paired}/text`)).trim());
    } catch (error) {
        const shown = gecko?.id
            ? await gecko.session('POST', '/execute/sync', { script: 'return document.body.innerText;', args: [] }).catch((reason) => `(${reason.message})`)
            : '(no session)';
        await gecko?.stop().catch((reason) => console.error(`closing Firefox: ${reason.message}`));
        report([
            [ 'Firefox: the extension\'s last status', JSON.stringify(seen.status) ],
            [ 'Firefox: the options page', shown ],
            ...hostSections(path.join(home, '.mozilla', 'native-messaging-hosts')),
            [ `geckodriver's and Firefox's log, with the add-on's console and keypaste's standard error (${log})`, readOr(log) ],
        ]);
        throw error;
    }
    await gecko.stop();
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

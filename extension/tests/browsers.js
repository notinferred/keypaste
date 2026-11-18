'use strict';

// What the real-browser gates share: the app driver holding the vault, waiting with a deadline, pairing through the driver,
// geckodriver's WebDriver for Firefox, and the report a failure prints.

const { spawn } = require('node:child_process');
const fs = require('node:fs');
const net = require('node:net');
const readline = require('node:readline');

const WAIT_MS = 60 * 1000;
const FIREFOX_ID = 'browser@keypaste.invalid';
const ELEMENT = 'element-6066-11e4-a52e-4f735466cecf';

// The background's own answer to get_status, without asking keypaste again.
const STATUS_ACTION = { action: 'get_status', args: [ true ] };

const escape = (text) => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

const pause = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

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

// What a failure leaves to read, each part in its own collapsed group of the job's log.
const report = function(sections) {
    for (const [ title, text ] of sections) {
        console.error(`::group::${title}`);
        console.error(text || '(empty)');
        console.error('::endgroup::');
    }
};

// The extension's own state, read for a failure's report.
const statusOr = (status) => status().then(JSON.stringify, (reason) => `(${reason.message})`);

const withinWait = function(promise, what, ms = WAIT_MS) {
    let timer;
    const timeout = new Promise((_, reject) => {
        timer = setTimeout(() => reject(new Error(`timed out after ${ms / 1000}s waiting for ${what}`)), ms);
    });
    return Promise.race([ promise, timeout ]).finally(() => clearTimeout(timer));
};

// Polls check until it returns something other than undefined.
const until = (check, what, ms = WAIT_MS) => withinWait((async () => {
    for (;;) {
        const value = await check();
        if (value !== undefined) {
            return value;
        }
        await pause(200);
    }
})(), what, ms);

const freePort = () => new Promise((resolve, reject) => {
    const server = net.createServer();
    server.on('error', reject);
    server.listen(0, '127.0.0.1', () => {
        const { port } = server.address();
        server.close(() => resolve(port));
    });
});

// tests/Keypaste.AppDriver holding the vault as the app does, its prompts drawn and answered through its standard input.
class HeldApp {
    constructor(driverPath, vault, env) {
        this.waiters = [];
        this.process = spawn(driverPath, [ 'hold', vault ], { env, stdio: [ 'pipe', 'pipe', 'inherit' ] });
        readline.createInterface({ input: this.process.stdout }).on('line', (line) => {
            console.log(`driver: ${line}`);
            this.waiters = this.waiters.filter((waiter) => !waiter(line));
        });
        this.exited = new Promise((resolve) => this.process.on('exit', resolve));
    }

    send(command) {
        console.log(`driver< ${command}`);
        this.process.stdin.write(command + '\n');
    }

    // The next line from now on matching pattern.
    next(pattern, ms = WAIT_MS) {
        let waiter;
        const seen = new Promise((resolve) => {
            waiter = (line) => {
                if (!pattern.test(line)) {
                    return false;
                }
                resolve(line);
                return true;
            };
            this.waiters.push(waiter);
        });
        return withinWait(seen, `the driver to print ${pattern}`, ms)
            .finally(() => this.waiters = this.waiters.filter((other) => other !== waiter));
    }

    // Every line matching pattern from now until stop() is called, as watch.lines.
    watch(pattern) {
        const watch = { lines: [] };
        const waiter = (line) => {
            if (pattern.test(line)) {
                watch.lines.push(line);
            }
            return false;
        };
        this.waiters.push(waiter);
        watch.stop = () => this.waiters = this.waiters.filter((other) => other !== waiter);
        return watch;
    }

    async act(command, pattern) {
        const answer = this.next(pattern);
        this.send(command);
        return answer;
    }

    async held() {
        const held = await this.next(/^holding session /);
        if (held.includes('not served')) {
            throw new Error(held);
        }
    }

    // Turns keypaste on for browser; on Windows Chrome and Brave read one setting, so turning on one turns on both.
    async browserOn(browser) {
        const said = await this.act(`browser-on ${browser}`, new RegExp(`^browser-on ${escape(browser)}`));
        if (!said.startsWith(`browser-on ${browser}: `) && !said.startsWith(`browser-on ${browser} not offered: Settings shows Turn off`)) {
            throw new Error(said);
        }
    }

    async quit() {
        this.process.stdin.end();
        await withinWait(this.exited, 'the driver to shut down');
    }
}

// Presses Connect through press(), which brings keypaste's confirmation straight away, allows it once it shows a six-digit
// code and returns the pairing's name.
const pair = async function(app, build, press) {
    const prompt = app.next(/^pair-prompt identity=/);
    await press();
    const shown = await prompt;
    const match = /^pair-prompt identity=(.*) code=(\d{3} \d{3}) name=(.*)$/.exec(shown);
    if (!match || match[1] !== build) {
        throw new Error(`the prompt names ${match ? match[1] : shown}, not ${build}`);
    }
    await app.act('allow', /^pair-prompt withdrawn/);
    return match[3];
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

// The W3C WebDriver commands geckodriver answers, plus its own add-on install.
class Gecko {
    // geckodriver's log, with Firefox's standard output and error, where the hosts it starts write theirs, goes to log.
    static async start(geckodriverPath, env, log) {
        const port = await freePort();
        const output = fs.openSync(log, 'w');
        // --allow-system-access lets the session load the add-on's page from Firefox's own chrome, which WebDriver's navigation refuses.
        const child = spawn(geckodriverPath, [ '--port', String(port), '--log', 'info', '--allow-system-access' ], { env, stdio: [ 'ignore', output, output ] });
        fs.closeSync(output);
        const gecko = new Gecko(port, child);
        await until(async () => {
            try {
                return (await fetch(`${gecko.base}/status`)).ok || undefined;
            } catch {
                return undefined;
            }
        }, 'geckodriver to listen');
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

    // uuid fixes the add-on's internal UUID, which names its pages' moz-extension:// origin; prefs join the add-on's console.
    async open(firefoxBinary, uuid, prefs = {}, { headless = true, acceptInsecureCerts = false } = {}) {
        const session = await this.call('POST', '/session', {
            capabilities: {
                alwaysMatch: {
                    browserName: 'firefox',
                    acceptInsecureCerts,
                    'moz:firefoxOptions': {
                        binary: firefoxBinary,
                        args: headless ? [ '-headless' ] : [],
                        prefs: {
                            'extensions.webextensions.uuids': JSON.stringify({ [FIREFOX_ID]: uuid }),
                            'devtools.console.stdout.chrome': true,
                            'devtools.console.stdout.content': true,
                            ...prefs,
                        },
                    },
                },
            },
        });
        this.id = session.sessionId;
        return session.capabilities;
    }

    async element(selector) {
        return (await this.session('POST', '/element', { using: 'css selector', value: selector }))[ELEMENT];
    }

    async waitDisplayed(selector, displayed = true) {
        return until(async () => {
            // The page may not have built the element yet, which WebDriver answers as no such element.
            const element = await this.element(selector).catch(() => null);
            return element && await this.session('GET', `/element/${element}/displayed`) === displayed ? element : undefined;
        }, `${selector} to ${displayed ? 'show' : 'hide'}`);
    }

    // WebDriver navigates only to web pages, so the tab loads the add-on's page as Firefox's own chrome would.
    async openExtensionPage(url) {
        await this.session('POST', '/moz/context', { context: 'chrome' });
        await this.session('POST', '/execute/sync', {
            script: 'gBrowser.selectedBrowser.loadURI(Services.io.newURI(arguments[0]), { triggeringPrincipal: Services.scriptSecurityManager.getSystemPrincipal() });',
            args: [ url ],
        });
        await this.session('POST', '/moz/context', { context: 'content' });
        await until(async () => await this.session('GET', '/url') === url || undefined, `Firefox to show ${url}`);
    }

    // The options page's own browser.runtime.sendMessage.
    async ask(message) {
        return this.session('POST', '/execute/async', {
            script: 'const done = arguments[1]; browser.runtime.sendMessage(arguments[0]).then(done, (error) => done({ failed: String(error) }));',
            args: [ message ],
        });
    }

    evaluate(script, ...args) {
        return this.session('POST', '/execute/sync', { script, args });
    }

    // A trusted click at a point of the viewport, as a person's pointer makes one.
    async clickAt(x, y) {
        await this.session('POST', '/actions', {
            actions: [ {
                type: 'pointer',
                id: 'mouse',
                parameters: { pointerType: 'mouse' },
                actions: [
                    { type: 'pointerMove', x: Math.round(x), y: Math.round(y), origin: 'viewport' },
                    { type: 'pointerDown', button: 0 },
                    { type: 'pointerUp', button: 0 },
                ],
            } ],
        });
        await this.session('DELETE', '/actions');
    }

    // A trusted key press, sent to whatever has focus, as a person's keyboard makes one.
    async press(key) {
        const value = { Enter: '\uE007', ArrowDown: '\uE015' }[key];
        await this.session('POST', '/actions', {
            actions: [ { type: 'key', id: 'keyboard', actions: [ { type: 'keyDown', value }, { type: 'keyUp', value } ] } ],
        });
        await this.session('DELETE', '/actions');
    }

    async stop() {
        if (this.id) {
            await this.session('DELETE', '').catch((error) => console.error(`closing Firefox: ${error.message}`));
        }
        this.child.kill();
        await withinWait(this.exited, 'geckodriver to exit');
    }
}

module.exports = {
    WAIT_MS, ELEMENT, FIREFOX_ID, STATUS_ACTION,
    escape, pause, readOr, listOr, report, statusOr, withinWait, until, freePort,
    HeldApp, pair, requirePaired, Gecko,
};

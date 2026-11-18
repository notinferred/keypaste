#!/usr/bin/env node
'use strict';

// scripts/verify-extension-load.sh's browser half: tests/Keypaste.AppDriver holds the vault, turns the host on for Chromium and
// Firefox; each browser loads its unpacked build, its options page presses Connect once the extension has
// connected, the driver allows the pairing, and the page and Settings' list must both show it under that build. On a failure
// it prints what each browser and keypaste left: the extension's status, page and console, the host manifest and the logs.
//
// Usage: pair-in-browsers.js <root> <driver> <keypaste> <vault> <chromium build> <firefox build> <firefox> <geckodriver> <chrome id>

const { spawn } = require('node:child_process');
const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const readline = require('node:readline');
const { chromium } = require('@playwright/test');
const { WAIT_MS, FIREFOX_ID, STATUS_ACTION, readOr, listOr, report, statusOr, withinWait, HeldApp, pair, requirePaired, Gecko } = require('./browsers');

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

const hostSections = (manifestDirectory) => [
    [ `host manifests in ${manifestDirectory}`, listOr(manifestDirectory) ],
    [ 'com.keypaste.browser.json', readOr(path.join(manifestDirectory, 'com.keypaste.browser.json')) ],
    [ 'the owner\'s audit', readOr(path.join(keypasteHome, 'audit.jsonl')) ],
];

// The vault's pipe is named from the person's profile, so the owner and the browser's keypaste share one HOME.
const holdApp = () => new HeldApp(driverPath, vault, {
    ...inherited,
    HOME: home,
    KEYPASTE_HOME: keypasteHome,
    KEYPASTE_DRIVER_PASSWORD: password,
    KEYPASTE_DRIVER_BROWSER_PROGRAM: keypaste,
    KEYPASTE_DRIVER_BROWSER_HOME: home,
    KEYPASTE_DRIVER_BROWSER_USERPROFILE: home,
    KEYPASTE_DRIVER_BROWSER_XDG_CONFIG_HOME: configHome,
    KEYPASTE_DRIVER_BROWSER_APPDATA: path.join(root, 'appdata'),
    KEYPASTE_DRIVER_BROWSER_LOCALAPPDATA: path.join(root, 'localappdata'),
    KEYPASTE_DRIVER_BROWSER_REGISTRY_ROOT: 'Software\\keypaste-tests\\extension-load',
});

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
        // Connect is pressed at once, while the extension may still be starting keypaste, as a person might.
        const name = await pair(app, build, () => page.locator('#connect-button').click());
        await page.locator('#paired').waitFor({ state: 'visible', timeout: WAIT_MS });
        await requirePaired(app, build, name, (await page.locator('#paired').textContent()).trim());
    } catch (error) {
        const shown = page ? await page.locator('body').innerText().catch((reason) => `(${reason.message})`) : '(no page)';
        const status = page ? await statusOr(() => page.evaluate((message) => browser.runtime.sendMessage(message), STATUS_ACTION)) : '(no page)';
        await stop().catch((reason) => console.error(`closing Chromium: ${reason.message}`));
        report([
            [ 'Chromium: the extension\'s status', status ],
            [ 'Chromium: the options page', shown ],
            [ 'Chromium: the extension\'s console', consoles.join('\n') ],
            ...hostSections(path.join(configHome, 'chromium', 'NativeMessagingHosts')),
            [ `Chromium's log, with keypaste's standard error (${log})`, readOr(log) ],
        ]);
        throw error;
    }
    await stop();
};

const pairFirefox = async function(app) {
    const build = `keypaste's extension from Firefox Add-ons (${FIREFOX_ID})`;
    const uuid = crypto.randomUUID();
    // Firefox on Linux reads a user's host manifests from ~/.mozilla/native-messaging-hosts, under the HOME the driver set Firefox up in.
    const log = path.join(root, 'geckodriver.log');
    let gecko;
    try {
        gecko = await Gecko.start(geckodriverPath, browserEnvironment, log);
        await gecko.open(firefoxBinary, uuid);
        const installed = await gecko.session('POST', '/moz/addon/install', { path: firefoxBuild, temporary: true });
        if (installed !== FIREFOX_ID) {
            throw new Error(`Firefox installed the add-on as ${installed}, not ${FIREFOX_ID}`);
        }

        await gecko.openExtensionPage(`moz-extension://${uuid}/options/options.html`);
        await gecko.waitDisplayed('#paired', false);
        const connect = await gecko.element('#connect-button');
        const name = await pair(app, build, () => gecko.session('POST', `/element/${connect}/click`, {}));
        const paired = await gecko.waitDisplayed('#paired');
        await requirePaired(app, build, name, (await gecko.session('GET', `/element/${paired}/text`)).trim());
    } catch (error) {
        const shown = gecko?.id
            ? await gecko.session('POST', '/execute/sync', { script: 'return document.body.innerText;', args: [] }).catch((reason) => `(${reason.message})`)
            : '(no session)';
        const status = gecko?.id ? await statusOr(() => gecko.ask(STATUS_ACTION)) : '(no session)';
        await gecko?.stop().catch((reason) => console.error(`closing Firefox: ${reason.message}`));
        report([
            [ 'Firefox: the extension\'s status', status ],
            [ 'Firefox: the options page', shown ],
            ...hostSections(path.join(home, '.mozilla', 'native-messaging-hosts')),
            [ `geckodriver's and Firefox's log, with the add-on's console and keypaste's standard error (${log})`, readOr(log) ],
        ]);
        throw error;
    }
    await gecko.stop();
};

(async () => {
    const app = holdApp();
    let code = 0;
    try {
        await app.held();
        for (const browser of [ 'chromium', 'firefox' ]) {
            await app.browserOn(browser);
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

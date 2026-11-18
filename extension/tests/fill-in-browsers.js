#!/usr/bin/env node
'use strict';

// scripts/verify-browser-fill.sh's browser half. tests/Keypaste.AppDriver holds a vault of logins and answers keypaste's
// prompts; each browser loads keypaste's extension, pairs, and visits pages a local HTTPS server serves under test names, where
// a trusted click on the field's key icon opens keypaste's list of names and trusted keys pick one, as a person's do. Each
// page's fields and the owner's log must show what keypaste's rules allow: the list written as one line and no password before
// a pick, the first pick on a site asked in keypaste and a later one filled at once, a subdomain listing the site's logins and
// asked at its own first pick, a lookalike and another tenant of a shared host listing nothing, an http page and a form posting
// elsewhere asked at every pick, and a cross-origin frame refused by the extension itself.
//
// Usage: fill-in-browsers.js <settings.json>, written by verify-browser-fill.sh:
//   { root, driver, keypaste, vault, real, geckodriver, cert, key, chromiumBuild, edgeBuild, firefoxBuild, chromeId, edgeId,
//     browsers: [ { name: chromium|chrome|edge|brave|firefox, executable } ] }
// With real set, keypaste is turned on in the browsers' own places for this user, as on a runner that is thrown away;
// otherwise in folders under root.

const { spawn, spawnSync } = require('node:child_process');
const crypto = require('node:crypto');
const fs = require('node:fs');
const http = require('node:http');
const https = require('node:https');
const os = require('node:os');
const path = require('node:path');
const readline = require('node:readline');
const { chromium } = require('@playwright/test');
const { ELEMENT, FIREFOX_ID, STATUS_ACTION, escape, pause, readOr, listOr, report, statusOr, withinWait, until, freePort,
    HeldApp, pair, Gecko } = require('./browsers');

const settings = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const { root, driver, keypaste, vault, real, geckodriver } = settings;

// The build each browser loads, the ID its store gives it, and how keypaste names it: Chromium and Brave take the Chrome Web
// Store's build, and Edge its own under the Edge test key.
const chromeBuild = { build: settings.chromiumBuild, id: settings.chromeId, identity: `keypaste's extension from the Chrome Web Store (${settings.chromeId})` };
const BUILDS = {
    chromium: chromeBuild,
    chrome: chromeBuild,
    brave: chromeBuild,
    edge: { build: settings.edgeBuild, id: settings.edgeId, identity: `keypaste's extension from Microsoft Edge Add-ons (${settings.edgeId})` },
    firefox: { build: settings.firefoxBuild, identity: `keypaste's extension from Firefox Add-ons (${FIREFOX_ID})` },
};
const browsers = settings.browsers.map((browser) => ({ ...browser, ...BUILDS[browser.name] }));
const { KEYPASTE_DRIVER_PASSWORD: master, ...inherited } = process.env;
if (!master) {
    console.error('KEYPASTE_DRIVER_PASSWORD names no master password');
    process.exit(2);
}

// How long a page is watched for a prompt that must not come.
const QUIET_MS = 5000;
const platform = process.platform;
const keypasteHome = path.join(root, 'keypaste-home');
const home = real ? os.homedir() : path.join(root, 'home');
const configHome = real ? (process.env.XDG_CONFIG_HOME || path.join(home, '.config')) : path.join(root, 'config');
const localAppData = real ? process.env.LOCALAPPDATA : path.join(root, 'localappdata');
const appData = real ? process.env.APPDATA : path.join(root, 'appdata');
const auditLog = path.join(keypasteHome, 'audit.jsonl');

// The places, as Core's HostPlaces names them, where each browser keeps its profiles and reads keypaste's manifest.
const hostPlaceDir = function(name) {
    const folder = {
        darwin: { chromium: 'Chromium', chrome: 'Google/Chrome', edge: 'Microsoft Edge', brave: 'BraveSoftware/Brave-Browser' },
        linux: { chromium: 'chromium', chrome: 'google-chrome', edge: 'microsoft-edge', brave: 'BraveSoftware/Brave-Browser' },
    }[platform]?.[name];
    if (platform === 'darwin') {
        return path.join(home, 'Library', 'Application Support', folder);
    }
    if (platform === 'linux') {
        return path.join(configHome, folder);
    }
    // Windows reads the manifest through the registry, so any folder holds the profile.
    return path.join(root, 'profiles', name);
};

// A browser refuses DevTools on its default profile folder, so each runs in a folder of its own whose NativeMessagingHosts is
// the one keypaste wrote; Windows reads the registry wherever the profile is.
const userDataDir = function(name) {
    if (platform === 'win32') {
        return hostPlaceDir(name);
    }
    const own = path.join(root, 'profiles', name);
    fs.mkdirSync(own, { recursive: true });
    const hosts = path.join(own, 'NativeMessagingHosts');
    fs.rmSync(hosts, { force: true });
    fs.symlinkSync(manifestDirectory(name), hosts, 'dir');
    return own;
};

const manifestDirectory = function(name) {
    if (platform === 'win32') {
        return path.join(localAppData, 'keypaste', 'NativeMessagingHosts');
    }
    if (name === 'firefox') {
        return platform === 'darwin'
            ? path.join(home, 'Library', 'Application Support', 'Mozilla', 'NativeMessagingHosts')
            : path.join(home, '.mozilla', 'native-messaging-hosts');
    }
    // Brave on macOS reads Chrome's folder, where keypaste writes its manifest (HostPlaces.Slot).
    return path.join(hostPlaceDir(platform === 'darwin' && name === 'brave' ? 'chrome' : name), 'NativeMessagingHosts');
};

const ownerEnvironment = {
    ...inherited,
    HOME: home,
    KEYPASTE_HOME: keypasteHome,
    KEYPASTE_DRIVER_PASSWORD: master,
    KEYPASTE_DRIVER_BROWSER_PROGRAM: keypaste,
    KEYPASTE_DRIVER_BROWSER_HOME: home,
    KEYPASTE_DRIVER_BROWSER_USERPROFILE: real ? os.homedir() : home,
    KEYPASTE_DRIVER_BROWSER_XDG_CONFIG_HOME: configHome,
    KEYPASTE_DRIVER_BROWSER_APPDATA: appData,
    KEYPASTE_DRIVER_BROWSER_LOCALAPPDATA: localAppData,
    KEYPASTE_DRIVER_BROWSER_REGISTRY_ROOT: real ? 'Software' : 'Software\\keypaste-tests\\browser-fill',
};
// What each browser passes to the keypaste it starts: the user folders the host was set up in, and the held vault.
const browserEnvironment = { ...inherited, HOME: home, XDG_CONFIG_HOME: configHome, KEYPASTE_HOME: keypasteHome, KEYPASTE_VAULT: vault };

// The pages. Every name resolves to this machine; the certificate names them all.
const NAMES = {
    shop: 'shop.test',
    www: 'www.shop.test',
    frame: 'login.shop.test',
    lookalike: 'shop.test.evil.test',
    other: 'pay.other.test',
    mail: 'mail.test',
    alice: 'alice.github.io',
    bob: 'bob.github.io',
    router: 'router.test',
};

const field = 'display:block;box-sizing:border-box;width:320px;height:40px;margin:12px;font-size:16px';
const form = (action) => `<form method="post" action="${action}">
<input id="username" name="username" autocomplete="username" style="${field}">
<input id="password" name="password" type="password" autocomplete="current-password" style="${field}">
<button type="submit" style="margin:12px">Sign in</button>
</form>`;
const pageOf = (title, body) => `<!doctype html><html><head><meta charset="utf-8"><title>${title}</title></head><body style="margin:0">${body}</body></html>`;

// The forms the pages posted, which a fill never does.
const submitted = [];

const serve = function(port, httpsPort) {
    return (request, response) => {
        const url = new URL(request.url, `https://${request.headers.host}`);
        let body;
        if (url.pathname === '/login') {
            body = pageOf('Sign in', form('/session'));
        } else if (url.pathname === '/cross') {
            body = pageOf('Sign in', form(`https://${NAMES.other}:${httpsPort}/session`));
        } else if (url.pathname === '/framed') {
            body = pageOf('Framed', `<iframe id="frame" src="https://${NAMES.frame}:${httpsPort}/login?next=/frame" style="width:400px;height:240px;border:0"></iframe>`);
        } else {
            if (request.method === 'POST') {
                submitted.push(`${request.headers.host}${url.pathname}`);
            }
            response.writeHead(request.method === 'POST' ? 204 : 404);
            response.end();
            return;
        }
        response.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
        response.end(body);
    };
};

const startServers = async function() {
    const httpsPort = await freePort();
    const httpPort = await freePort();
    const secure = https.createServer({ cert: fs.readFileSync(settings.cert), key: fs.readFileSync(settings.key) }, serve(httpsPort, httpsPort));
    const plain = http.createServer(serve(httpPort, httpsPort));
    await Promise.all([ [ secure, httpsPort ], [ plain, httpPort ] ].map(([ server, port ]) =>
        new Promise((resolve, reject) => server.on('error', reject).listen(port, '127.0.0.1', resolve))));
    return { httpsPort, httpPort, close: () => Promise.all([ secure, plain ].map((server) => new Promise((resolve) => server.close(resolve)))) };
};

// The logins, each a password no page and no log line may show.
const logins = (ports) => [
    { title: 'Shop', username: 'dana', password: `shop-dana-${crypto.randomUUID()}`, url: `https://${NAMES.shop}:${ports.httpsPort}` },
    { title: 'Shop work', username: 'sam', password: `shop-sam-${crypto.randomUUID()}`, url: `https://${NAMES.shop}:${ports.httpsPort}` },
    { title: 'Mail', username: 'dana', password: `mail-dana-${crypto.randomUUID()}`, url: `https://${NAMES.mail}:${ports.httpsPort}` },
    { title: 'Pages', username: 'alice', password: `pages-alice-${crypto.randomUUID()}`, url: `https://${NAMES.alice}:${ports.httpsPort}` },
    { title: 'Router', username: 'admin', password: `router-admin-${crypto.randomUUID()}`, url: `http://${NAMES.router}:${ports.httpPort}` },
];

const addLogins = function(items) {
    for (const item of items) {
        const added = spawnSync(keypaste, [ 'add', item.title, '--username', item.username, '--url', item.url, '--notes', '', '--vault', vault ], {
            env: { ...inherited, HOME: home, KEYPASTE_HOME: keypasteHome },
            input: `${master}\n${item.password}\n`,
            encoding: 'utf8',
        });
        if (added.status !== 0) {
            throw new Error(`keypaste add ${item.title} exited ${added.status}: ${added.stderr}`);
        }
    }
};

// What the page shows: its fields' values, and how many of the extension's own elements sit in its body, where the pages put
// no div. The extension holds an icon's wrapper at 'all: unset' (ui.js), and shows a notification such as "no logins found"
// in one it displays.
const PAGE_STATE = `
    const value = (id) => document.getElementById(id)?.value ?? null;
    const ours = [ ...document.body.children ].filter((element) => element.tagName === 'DIV');
    const rect = document.getElementById('username')?.getBoundingClientRect();
    return {
        username: value('username'),
        password: value('password'),
        icons: ours.filter((element) => element.style.cssText === 'all: unset;').length,
        notices: ours.filter((element) => element.style.display === 'block').length,
        divs: ours.length,
        styles: ours.map((element) => element.style.cssText.slice(0, 60)),
        field: rect ? { left: rect.left, top: rect.top, width: rect.width, height: rect.height } : null,
    };`;

// Where the extension draws the key icon on a 40-pixel field: its right end, centred, as icon-handler.js places it.
const iconPoint = (rect) => ({ x: rect.left + rect.width - 19, y: rect.top + rect.height / 2 });


// A Chromium-family browser started here, so its standard error, where the hosts it starts write theirs, reaches a file. The
// extension is loaded over the DevTools pipe, as branded Chrome no longer loads one from its command line, and pages are driven
// through the DevTools port.
const launchChromiumFamily = async function(browser, log, spki) {
    // The browser's own password manager stays off, so whatever fills a page is keypaste.
    const preferences = path.join(userDataDir(browser.name), 'Default', 'Preferences');
    fs.mkdirSync(path.dirname(preferences), { recursive: true });
    fs.writeFileSync(preferences, JSON.stringify({ credentials_enable_service: false, profile: { password_manager_enabled: false } }));
    const child = spawn(browser.executable, [
        '--headless',
        '--no-sandbox',
        '--no-first-run',
        '--no-default-browser-check',
        '--disable-background-networking',
        '--disable-component-update',
        '--disable-sync',
        '--password-store=basic',
        '--use-mock-keychain',
        '--enable-logging=stderr',
        '--v=0',
        '--remote-debugging-pipe',
        '--remote-debugging-port=0',
        '--enable-unsafe-extension-debugging',
        `--host-resolver-rules=${Object.values(NAMES).map((name) => `MAP ${name} 127.0.0.1`).join(',')}`,
        `--ignore-certificate-errors-spki-list=${spki}`,
        '--disable-features=HttpsUpgrades',
        `--user-data-dir=${userDataDir(browser.name)}`,
        'about:blank',
    ], { env: browserEnvironment, stdio: [ 'ignore', 'pipe', 'pipe', 'pipe', 'pipe' ] });
    const exited = new Promise((resolve) => child.on('exit', resolve));
    const out = fs.createWriteStream(log);
    child.stdout.pipe(out, { end: false });
    let devtools;
    const stop = async () => {
        await devtools?.close().catch(() => {});
        child.kill();
        await withinWait(exited, `${browser.name} to exit`);
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
            exited.then((code) => reject(new Error(`${browser.name} exited with ${code} before it listened for DevTools`)));
        }), `${browser.name} to listen for DevTools`);

        // The pipe's messages are JSON, each ended by a NUL.
        const loaded = withinWait(new Promise((resolve, reject) => {
            let pending = '';
            child.stdio[4].on('data', (chunk) => {
                pending += chunk.toString('utf8');
                let end;
                while ((end = pending.indexOf('\0')) >= 0) {
                    const message = JSON.parse(pending.slice(0, end));
                    pending = pending.slice(end + 1);
                    if (message.id === 1) {
                        message.error ? reject(new Error(`Extensions.loadUnpacked: ${message.error.message}`)) : resolve(message.result.id);
                    }
                }
            });
        }), `${browser.name} to load the extension`);
        child.stdio[3].write(JSON.stringify({ id: 1, method: 'Extensions.loadUnpacked', params: { path: browser.build } }) + '\0');
        const id = await loaded;
        if (id !== browser.id) {
            throw new Error(`${browser.name} loaded the extension as ${id}, not ${browser.id}`);
        }

        devtools = await chromium.connectOverCDP(endpoint);
        const context = devtools.contexts()[0];
        const version = devtools.version();
        return { context, version, stop };
    } catch (error) {
        await stop();
        throw error;
    }
};

// A page of a Chromium-family browser, through Playwright.
class ChromiumTab {
    constructor(page) {
        this.page = page;
    }

    async goto(url) {
        await this.page.goto(url, { waitUntil: 'load' });
    }

    async reload() {
        await this.page.reload({ waitUntil: 'load' });
    }

    // The page's state, in its frame when framed names one.
    async state(framed = false) {
        const frame = framed ? await (await this.page.$('#frame')).contentFrame() : this.page.mainFrame();
        const state = await frame.evaluate(`(() => {${PAGE_STATE}})()`);
        if (framed && state.field) {
            const box = await (await this.page.$('#frame')).boundingBox();
            state.field.left += box.x;
            state.field.top += box.y;
        }
        return state;
    }

    async clickAt({ x, y }) {
        await this.page.mouse.click(x, y);
    }

    async press(key) {
        await this.page.keyboard.press(key);
    }

    // What the page's own document has at a point: a shadow root the extension draws in shows as its host.
    at({ x, y }) {
        return this.page.evaluate(`document.elementFromPoint(${x}, ${y})?.tagName ?? ''`);
    }

}

// A page of Firefox, through geckodriver.
class FirefoxTab {
    constructor(gecko) {
        this.gecko = gecko;
    }

    async goto(url) {
        await this.gecko.session('POST', '/url', { url });
    }

    async reload() {
        await this.gecko.session('POST', '/refresh', {});
    }

    async state(framed = false) {
        if (!framed) {
            return this.gecko.evaluate(PAGE_STATE);
        }
        const frame = await this.gecko.element('#frame');
        const box = await this.gecko.session('GET', `/element/${frame}/rect`);
        await this.gecko.session('POST', '/frame', { id: { [ELEMENT]: frame } });
        try {
            const state = await this.gecko.evaluate(PAGE_STATE);
            if (state.field) {
                state.field.left += box.x;
                state.field.top += box.y;
            }
            return state;
        } finally {
            await this.gecko.session('POST', '/frame/parent', {});
        }
    }

    async clickAt({ x, y }) {
        await this.gecko.clickAt(x, y);
    }

    async press(key) {
        await this.gecko.press(key);
    }

    at({ x, y }) {
        return this.gecko.evaluate(`return document.elementFromPoint(${x}, ${y})?.tagName ?? '';`);
    }

}

const auditText = () => readOr(auditLog);


// The acts every browser performs, on one pairing of its own.
const fillActs = async function(app, tab, ports, items, name, pairing) {
    const [ shopDana, shopSam, mail, , router ] = items;
    const at = (host, pagePath, port = ports.httpsPort, scheme = 'https') => `${scheme}://${host}:${port}${pagePath}`;
    const origin = (host, port = ports.httpsPort, scheme = 'https') => `${scheme}://${host}:${port}`;

    // The page's fields have their icon once the extension has read the page, and on the page itself the icon is under the
    // point a click aims at; a frame's document is not the page's, so there the icon's wrapper alone is waited for.
    const ready = (framed = false) => until(async () => {
        const state = await tab.state(framed);
        if (!state.field || state.icons === 0) {
            return undefined;
        }
        return framed || (await tab.at(iconPoint(state.field))).startsWith('DIV') ? state : undefined;
    }, `${name} to draw keypaste's icon`);

    // A click on the icon opens keypaste's list beside the field, a div more in the page, and writes one line when it names a
    // login. Brave has let a first click on a page just opened do nothing (BACKLOG), so one more click is allowed and printed.
    const opened = async (framed = false) => {
        const before = (await ready(framed)).divs;
        const point = iconPoint((await ready(framed)).field);
        await tab.clickAt(point);
        await until(async () => (await tab.state(framed)).divs > before || undefined, `${name} to open keypaste's list`, QUIET_MS * 2).catch(async () => {
            console.log(`${name}: a click brought no list; clicking again`);
            await tab.clickAt(point);
            await until(async () => (await tab.state(framed)).divs > before || undefined, `${name} to open keypaste's list`);
        });
    };

    // The list's line for an origin, naming how many logins it showed and no password.
    const listed = (expectedOrigin, count, since) => until(async () => {
        const lines = auditText().slice(since).split('\n').filter((line) => line.includes('"field":"list"') && line.includes(expectedOrigin));
        return lines.find((line) => line.includes(`listed ${count} login(s) by name, no password`));
    }, `${name}'s list of ${count} login(s) on ${expectedOrigin} to be recorded`);

    // Opens the list on the page, waits for its line, and picks the login at row (from 1) with the keys, focus being in the field.
    const pickRow = async (expectedOrigin, count, row) => {
        const since = auditText().length;
        await opened();
        await listed(expectedOrigin, count, since);
        // A pick counts only once the list has been on screen a moment (autocomplete.js).
        await pause(800);
        for (let i = 1; i < row; i++) {
            await tab.press('ArrowDown');
        }
        await tab.press('Enter');
    };

    // A pick keypaste must ask about, for the login and origin given, which the driver then answers.
    const asked = async (expectedOrigin, count, row, item) => {
        const prompt = app.next(/^fill-prompt /, QUIET_MS * 3);
        await pickRow(expectedOrigin, count, row);
        const shown = await prompt;
        const host = new URL(expectedOrigin).hostname;
        const match = /^fill-prompt question=(.*) origin=(\S+) pairing=(.*) login=(.*?)( note=.*)?$/.exec(shown);
        if (!match || match[1] !== `Fill ${item.title} on ${host}?` || match[2] !== expectedOrigin || match[3] !== pairing) {
            throw new Error(`${name}: expected keypaste to ask to fill ${item.title} on ${expectedOrigin} under ${pairing}, saw ${shown}`);
        }
        return { note: (match[5] ?? '').trim() };
    };

    const filledWith = (item, framed = false) => {
        let seen;
        return until(async () => {
            seen = await tab.state(framed);
            return seen.username === item.username && seen.password === item.password ? seen : undefined;
        }, `${name} to fill ${item.title}`).catch((error) => {
            throw new Error(`${error.message}; the page showed ${seen?.username} with ${seen?.password ? 'a password' : 'no password'}, `
                + `forms posted: ${submitted.join(', ') || 'none'}`);
        });
    };

    // A pick keypaste fills at once, as a login filled on that exact site before is.
    const atOnce = async (expectedOrigin, count, row, item) => {
        const prompts = app.watch(/^fill-prompt /);
        await pickRow(expectedOrigin, count, row);
        await filledWith(item);
        prompts.stop();
        if (prompts.lines.length > 0) {
            throw new Error(`${name}: a login filled on ${expectedOrigin} before was asked again: ${prompts.lines[0]}`);
        }
    };

    // A click that lists nothing: the list opens, keypaste records no list line for the host and asks nobody, and the fields stay empty.
    const nothingListed = async (what, host, framed = false) => {
        const prompts = app.watch(/^fill-prompt /);
        const since = auditText().length;
        await opened(framed);
        await pause(1000);
        await tab.press('Enter');
        await pause(1000);
        prompts.stop();
        if (prompts.lines.length > 0) {
            throw new Error(`${name}: ${what} brought a prompt: ${prompts.lines[0]}`);
        }
        if (auditText().slice(since).split('\n').some((line) => line.includes('"field":"list"') && line.includes(host))) {
            throw new Error(`${name}: ${what} listed a login`);
        }
        const state = await tab.state(framed);
        if (state.username || state.password) {
            throw new Error(`${name}: ${what} filled ${state.username}`);
        }
    };

    const answer = (command) => app.act(command, /^fill-prompt withdrawn/);
    const emptied = async () => {
        await pause(1000);
        const state = await tab.state();
        if (state.username || state.password) {
            throw new Error(`${name}: a refused pick filled ${state.username}`);
        }
    };

    // The item's own site lists both of its logins by name; the first pick there is asked in keypaste, and fills once allowed.
    await tab.goto(at(NAMES.shop, '/login?next=/account'));
    await asked(origin(NAMES.shop), 2, 1, shopDana);
    await answer('allow');
    await filledWith(shopDana);

    // A later pick of that login on that exact site fills at once; the other login's first pick there is still asked.
    await tab.reload();
    await atOnce(origin(NAMES.shop), 2, 1, shopDana);
    await tab.reload();
    await asked(origin(NAMES.shop), 2, 2, shopSam);
    await answer('allow');
    await filledWith(shopSam);

    // A subdomain of the item's site lists its logins, and the first pick there is asked though the site was filled before.
    await tab.goto(at(NAMES.www, '/login'));
    await asked(origin(NAMES.www), 2, 1, shopDana);
    await answer('deny');
    await emptied();

    // A lookalike name and another tenant of a shared host list nothing, and nobody is asked.
    await tab.goto(at(NAMES.lookalike, '/login'));
    await nothingListed('a lookalike of the item\'s site', NAMES.lookalike);
    await tab.goto(at(NAMES.bob, '/login'));
    await nothingListed('another tenant of github.io', NAMES.bob);

    // An http page is asked at every pick, saying so; a refusal fills nothing.
    await tab.goto(at(NAMES.router, '/login', ports.httpPort, 'http'));
    let shown = await asked(origin(NAMES.router, ports.httpPort, 'http'), 1, 1, router);
    if (!shown.note.includes('not secure')) {
        throw new Error(`${name}: an http page's prompt said ${shown.note}`);
    }
    await answer('allow');
    await filledWith(router);
    await tab.reload();
    await asked(origin(NAMES.router, ports.httpPort, 'http'), 1, 1, router);
    await answer('deny');
    await emptied();

    // A form that posts to another site is asked at every pick, even of a login filled on the page's site before, naming that site.
    await tab.goto(at(NAMES.shop, '/cross'));
    shown = await asked(origin(NAMES.shop), 2, 1, shopDana);
    if (!shown.note.includes(NAMES.other)) {
        throw new Error(`${name}: a form posting to ${NAMES.other} was asked with ${shown.note}`);
    }
    await answer('deny');
    await emptied();

    // A site with one login: the first pick asked, the next filled at once, and Settings lists it as filled there before.
    await tab.goto(at(NAMES.mail, '/login'));
    await asked(origin(NAMES.mail), 1, 1, mail);
    await answer('allow');
    await filledWith(mail);
    await tab.reload();
    await atOnce(origin(NAMES.mail), 1, 1, mail);
    await app.act('allowances', new RegExp(`^allowance item=Mail on ${escape(origin(NAMES.mail))} in ${escape(pairing)}$`));

    // A login form in a frame from another origin, even of the item's own site, is refused by the extension before keypaste.
    const before = auditText();
    await tab.goto(at(NAMES.shop, '/framed'));
    await nothingListed('a cross-origin frame', NAMES.frame, true);
    if (auditText().slice(before.length).includes(NAMES.frame)) {
        throw new Error(`${name}: the cross-origin frame reached keypaste`);
    }
};

const requireAudit = function(items, ports) {
    const log = auditText();
    for (const item of items) {
        if (log.includes(item.password)) {
            throw new Error(`the owner's log holds ${item.title}'s password`);
        }
    }
    for (const leak of [ 'next=', '/login', '/account', '/cross' ]) {
        if (log.includes(leak)) {
            throw new Error(`the owner's log holds a page's path or query: ${leak}`);
        }
    }
    for (const expected of [ `https://${NAMES.shop}:${ports.httpsPort}`, `http://${NAMES.router}:${ports.httpPort}`, `https://${NAMES.mail}:${ports.httpsPort}` ]) {
        if (!log.includes(expected)) {
            throw new Error(`the owner's log names no fill on ${expected}`);
        }
    }
};

const runChromiumFamily = async function(app, browser, ports, items, spki) {
    const log = path.join(root, `${browser.name}.log`);
    const consoles = [];
    let page;
    let stop = async () => {};
    try {
        const launched = await launchChromiumFamily(browser, log, spki);
        stop = launched.stop;
        console.log(`${browser.name}: ${launched.version}`);
        const { context } = launched;
        const listen = (worker) => worker.on('console', (message) => consoles.push(`worker ${message.type()}: ${message.text()}`));
        context.on('serviceworker', listen);
        context.serviceWorkers().forEach(listen);

        page = await context.newPage();
        page.on('console', (message) => consoles.push(`page ${message.type()}: ${message.text()}`));
        page.on('pageerror', (error) => consoles.push(`page error: ${error.message}`));
        await page.goto(`chrome-extension://${browser.id}/options/options.html`);
        await page.locator('#paired').waitFor({ state: 'hidden' });
        // Connect is pressed at once, while the extension's worker may still be starting, as a person might.
        const pairing = await pair(app, browser.identity, () => page.locator('#connect-button').click());
        await page.locator('#paired').waitFor({ state: 'visible' });
        await fillActs(app, new ChromiumTab(page), ports, items, browser.name, pairing);
    } catch (error) {
        const shown = page ? await new ChromiumTab(page).state().then((state) => `${page.url()} ${JSON.stringify(state)}`, (reason) => `(${reason.message})`) : '(no page)';
        const status = page?.url().startsWith('chrome-extension://')
            ? await statusOr(() => page.evaluate((message) => chrome.runtime.sendMessage(message), STATUS_ACTION))
            : '(not on the extension\'s page)';
        await stop().catch((reason) => console.error(`closing ${browser.name}: ${reason.message}`));
        report([
            [ `${browser.name}: the page`, shown ],
            [ `${browser.name}: the extension's status`, status ],
            [ `${browser.name}: the extension's console`, consoles.join('\n') ],
            [ `host manifests in ${manifestDirectory(browser.name)}`, listOr(manifestDirectory(browser.name)) ],
            [ 'the owner\'s audit', auditText() ],
            [ `${browser.name}'s log, with keypaste's standard error (${log})`, readOr(log) ],
        ]);
        throw error;
    }
    await stop();
};

const runFirefox = async function(app, browser, ports, items) {
    const uuid = crypto.randomUUID();
    const log = path.join(root, 'geckodriver-fill.log');
    let gecko;
    try {
        gecko = await Gecko.start(geckodriver, browserEnvironment, log);
        // Every name resolves to this machine, and the pages' certificate, made for this run, is accepted.
        const capabilities = await gecko.open(browser.executable, uuid, {
            'network.dns.forceResolve': '127.0.0.1',
            'dom.security.https_first': false,
            'signon.rememberSignons': false,
            'signon.autofillForms': false,
        }, { acceptInsecureCerts: true });
        console.log(`firefox: ${capabilities.browserVersion}`);
        const installed = await gecko.session('POST', '/moz/addon/install', { path: browser.build, temporary: true });
        if (installed !== FIREFOX_ID) {
            throw new Error(`Firefox installed the add-on as ${installed}, not ${FIREFOX_ID}`);
        }
        await gecko.openExtensionPage(`moz-extension://${uuid}/options/options.html`);
        await gecko.waitDisplayed('#paired', false);
        const connect = await gecko.element('#connect-button');
        const pairing = await pair(app, browser.identity, () => gecko.session('POST', `/element/${connect}/click`, {}));
        await gecko.waitDisplayed('#paired');
        await fillActs(app, new FirefoxTab(gecko), ports, items, 'firefox', pairing);
    } catch (error) {
        const shown = gecko?.id
            ? await new FirefoxTab(gecko).state().then(JSON.stringify, (reason) => `(${reason.message})`)
            : '(no session)';
        await gecko?.stop().catch((reason) => console.error(`closing Firefox: ${reason.message}`));
        report([
            [ 'firefox: the page', shown ],
            [ `host manifests in ${manifestDirectory('firefox')}`, listOr(manifestDirectory('firefox')) ],
            [ 'the owner\'s audit', auditText() ],
            [ `geckodriver's and Firefox's log, with the add-on's console and keypaste's standard error (${log})`, readOr(log) ],
        ]);
        throw error;
    }
    await gecko.stop();
};

// The browser's keypaste ends when the browser does, so none outlives the run.
const requireNoHostLeft = async function(name) {
    const left = () => {
        const listed = platform === 'win32'
            ? spawnSync('tasklist', [ '/FO', 'CSV', '/NH' ], { encoding: 'utf8' }).stdout
            : spawnSync('ps', [ '-eo', 'args' ], { encoding: 'utf8' }).stdout;
        return listed.split(/\r?\n/).filter((line) => line.includes(keypaste) || (platform === 'win32' && /"keypaste\.exe"/i.test(line)));
    };
    await until(async () => left().length === 0 || undefined, `the keypaste ${name} started to end`, 15000).catch(() => {
        throw new Error(`a keypaste ${name} started is still running: ${left().join(' | ')}`);
    });
};

(async () => {
    const spki = crypto.createHash('sha256')
        .update(new crypto.X509Certificate(fs.readFileSync(settings.cert)).publicKey.export({ type: 'spki', format: 'der' }))
        .digest('base64');
    const ports = await startServers();
    const items = logins(ports);
    addLogins(items);
    fs.mkdirSync(keypasteHome, { recursive: true });

    const app = new HeldApp(driver, vault, ownerEnvironment);
    let code = 0;
    try {
        await app.held();
        for (const browser of browsers) {
            await app.browserOn(browser.name);
            browser.name === 'firefox'
                ? await runFirefox(app, browser, ports, items)
                : await runChromiumFamily(app, browser, ports, items, spki);
            await requireNoHostLeft(browser.name);
            // Turned off again, so the next browser finds only the manifest keypaste writes for it.
            await app.act(`browser-off ${browser.name}`, new RegExp(`^browser-off ${escape(browser.name)}`));
            console.log(`${browser.name} filled and refused as keypaste's rules say`);
        }
        requireAudit(items, ports);
    } catch (error) {
        console.error(`::error::${error.message}`);
        code = 1;
    } finally {
        await app.quit().catch((error) => {
            console.error(`::error::${error.message}`);
            app.process.kill('SIGKILL');
            code = 1;
        });
        await ports.close();
    }
    process.exit(code);
})();

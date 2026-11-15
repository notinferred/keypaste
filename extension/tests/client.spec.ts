import { test, expect } from '@playwright/test';
import { pathToFileURL } from 'node:url';

const TIMEOUT_OR_NOT_CONNECTED = 5;

// The background's native messaging client over a fake port that answers only when told to, as keypaste answers one request at a time.
test.beforeEach(async ({ page }) => {
    await page.goto(pathToFileURL('src/tests/tests.html').toString());
    await page.evaluate(() => {
        Object.assign(window, {
            tr: (key) => key,
            logError: () => {},
            debugLogMessage: () => {},
            keepass: { isKeypasteAvailable: true },
        });
    });
    await page.addScriptTag({ path: 'src/background/nacl.min.js' });
    await page.addScriptTag({ path: 'src/background/nacl-util.min.js' });
    await page.addScriptTag({ path: 'src/background/client.js' });
    await page.evaluate(() => {
        const sent = [];
        const settled = {};
        keepassClient.nativePort = { postMessage: (request) => sent.push(request) };
        Object.assign(window, {
            sent,
            settled,
            send: (name, action, enableTimeout) => {
                const request = { action, nonce: keepassClient.getNonce() };
                keepassClient.sendNativeMessage(request, enableTimeout, 200).then((response) => { settled[name] = response; });
            },
            answer: (index) => keepassClient.handleNativeMessage({ action: sent[index].action, nonce: keepassClient.incrementedNonce(sent[index].nonce) }),
        });
    });
});

test('A timed request waiting behind a pairing is not taken for keypaste being unreachable', async ({ page }) => {
    await page.evaluate(() => {
        send('associate', 'associate', false);
        send('hash', 'get-databasehash', true);
    });

    await page.waitForTimeout(600);
    expect(await page.evaluate(() => Object.keys(settled))).toEqual([]);
    expect(await page.evaluate(() => keepass.isKeypasteAvailable)).toBe(true);

    await page.evaluate(async () => {
        await answer(0);
        await answer(1);
    });

    await expect.poll(() => page.evaluate(() => [ settled.associate?.errorCode, settled.hash?.errorCode, settled.hash?.nonce ])).toEqual([
        undefined,
        undefined,
        await page.evaluate(() => keepassClient.incrementedNonce(sent[1].nonce)),
    ]);
});

test('A timed request with nothing ahead of it times out when keypaste does not answer', async ({ page }) => {
    await page.evaluate(() => send('hash', 'get-databasehash', true));

    await expect.poll(() => page.evaluate(() => settled.hash?.errorCode)).toBe(TIMEOUT_OR_NOT_CONNECTED);
    expect(await page.evaluate(() => keepass.isKeypasteAvailable)).toBe(false);
});

test('A timed request still times out once the request ahead of it is answered', async ({ page }) => {
    await page.evaluate(() => {
        send('associate', 'associate', false);
        send('hash', 'get-databasehash', true);
    });

    await page.waitForTimeout(400);
    await page.evaluate(() => answer(0));

    await expect.poll(() => page.evaluate(() => settled.hash?.errorCode)).toBe(TIMEOUT_OR_NOT_CONNECTED);
});

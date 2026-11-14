'use strict';

const contextMenuItems = [
    { title: tr('contextMenuFillUsernameAndPassword'), action: 'fill_username_password' }
];

const initListeners = async function() {
    // Clear credentials on reload so a new retrieval can be made
    browser.webNavigation.onCommitted.addListener(async (details) => {
        if (details.transitionType === 'reload') {
            page.clearLogins(details.tabId);
        }
    });

    // Main event listener
    browser.runtime.onMessage.addListener(kpxcEvent.onMessage);

    // Listen for keyboard shortcuts specified by user
    browser.commands.onCommand.addListener(async (command) => {
        if (contextMenuItems.some(e => e.action === command)) {
            const tab = await getCurrentTab();
            if (tab?.id) {
                browser.tabs.sendMessage(tab.id, { action: command });
            }
        }
    });

    browser.contextMenus.onClicked.addListener(async (item, tab) => {
        if (!tab?.id) {
            return;
        }

        browser.tabs.sendMessage(tab.id, {
            action: item.menuItemId
        }, {
            frameId: item.frameId
        }).catch((err) => {
            logError(err);
        });
    });
};

const initContextMenuItems = async function() {
    page.menuContexts = [ 'editable' ];
    if (page.isFirefox) {
        page.menuContexts.push('password');
    }

    // Create context menu items
    await browser.contextMenus.removeAll();
    for (const item of contextMenuItems) {
        try {
            await browser.contextMenus.create({
                title: item.title,
                contexts: page.menuContexts,
                id: item.action
            });
        } catch (e) {
            logError(e);
        }
    }
};

(async () => {
    try {
        await keepass.migrateKeyRing();
        await page.initBrowser();
        await tabs.initOpenedTabs();
        await tabs.initListeners();
        await initListeners();
        await initContextMenuItems();
        await keepass.reconnect(null, 5000); // 5 second timeout for the first connect
        await keepass.updateDatabase();
    } catch (e) {
        logError(`init.js failed: ${e}`);
    }
})();

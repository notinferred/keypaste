'use strict';

const browserActionWrapper = browser.action || browser.browserAction;
const browserAction = {};

// Chromium's toolbar takes no SVG, so it gets PNGs of the same marks
const TOOLBAR_ICONS = {
    normal: 'keypaste',
    locked: 'keypaste-locked',
    cross: 'keypaste-disconnected',
    bang: 'keypaste-unpaired',
};

browserAction.show = async function(tab, popupData) {
    popupData ??= page.popupData;
    page.popupData = popupData;

    browserActionWrapper.setIcon({
        path: await browserAction.generateIconName(popupData.iconType),
        tabId: tab.id
    });

    if (popupData.popup && tab?.id) {
        browserActionWrapper.setPopup({
            popup: `popups/${popupData.popup}.html`,
            tabId: tab.id
        });

        let badgeText = '';
        const currentTab = tabs.getTabFromId(tab.id);
        if (popupData.popup === 'popup_login') {
            badgeText = currentTab?.loginList.length;
        }

        browserAction.setBadgeText(tab?.id, badgeText);
    }
};

browserAction.showDefault = async function(tab) {
    const popupData = {
        iconType: 'normal',
        popup: 'popup'
    };

    const response = await keepass.isConfigured().catch((err) => {
        logError('Cannot show default popup: ' + err);
    });

    // This should not be possible. Database cannot be open without a hash.
    if (!keepass.isDatabaseClosed && !keepass.associated.hash) {
        return;
    }

    if (!response && !keepass.isKeypasteAvailable) {
        popupData.iconType = 'cross';
    } else if (!keepass.isAssociated() && !keepass.isDatabaseClosed) {
        popupData.iconType = 'bang';
    } else if (keepass.isKeypasteAvailable && keepass.isDatabaseClosed) {
        popupData.iconType = 'locked';
    }

    // Get the current tab if no tab given
    tab ??= await getCurrentTab();
    if (!tab) {
        return;
    }

    const currentTab = tabs.getTabFromId(tab?.id);
    if (currentTab?.loginList.length > 0) {
        popupData.iconType = 'normal';
        popupData.popup = 'popup_login';
        browserAction.setBadgeText(tab?.id, currentTab?.loginList.length);
    }

    await browserAction.show(tab, popupData);
};

browserAction.setBadgeText = function(tabId, badgeText) {
    if (!tabId) {
        return;
    }

    browserActionWrapper.setBadgeBackgroundColor({ color: '#2A2C30' });
    browserActionWrapper.setBadgeText({ text: String(badgeText), tabId: tabId });
};

browserAction.generateIconName = async function(iconType) {
    const name = TOOLBAR_ICONS[iconType] ?? TOOLBAR_ICONS.normal;
    if (page.isFirefox) {
        return `/icons/${name}.svg`;
    }

    return { 16: `/icons/${name}-16.png`, 32: `/icons/${name}-32.png` };
};

'use strict';

// keypaste decides these; the extension offers no settings of its own.
const defaultSettings = {
    afterFillSorting: SORT_BY_MATCHING_CREDENTIALS_SETTING,
    autoCompleteUsernames: true,
    autoFillSingleEntry: false,
    autoFillRelevantCredential: false,
    autoSubmit: false,
    clearCredentialsTimeout: 10,
    credentialSorting: SORT_BY_GROUP_AND_TITLE,
    debugLogging: false,
    showGroupNameInAutocomplete: true,
    showLoginFormIcon: true,
    showNotifications: true,
    useCompactMode: false,
    useObserver: true,
    usePredefinedSites: true,
};

const AUTO_SUBMIT_TIMEOUT = 5000;

/**
 * @Object page
 * Handles information between background and content scripts.
 */
const page = {};
page.autoSubmitPerformed = false;
page.currentRequest = {};
page.isFirefox = false;
page.manualFill = ManualFill.NONE;
page.menuContexts = [ 'editable' ];
page.passwordFilled = false;
page.settings = Object.freeze({ ...defaultSettings });

page.popupData = {
    iconType: 'normal',
    popup: 'popup'
};

page.initBrowser = async function() {
    page.isFirefox = isFirefox();
};

page.getSettings = async function() {
    return page.settings;
};

page.clearCredentials = async function(tabId, complete) {
    if (!tabs.getTabFromId(tabId)) {
        return;
    }

    page.passwordFilled = false;
    tabs.updateTabValues(tabId, { credentials: [] });

    if (complete) {
        page.clearLogins(tabId);

        browser.tabs.sendMessage(tabId, {
            action: 'clear_credentials'
        }).catch((e) => {});
    }
};

page.clearLogins = async function(tabId) {
    const currentTab = tabs.getTabFromId(tabId);
    if (!currentTab) {
        return;
    }

    tabs.updateTabValues(tabId, {
        credentials: [],
        loginList: []
    });

    page.currentRequest = {};
    page.passwordFilled = false;
};

// Clear all logins from all pages and update the content scripts
page.clearAllLogins = function() {
    tabs.tabList.forEach((_tab, key) => {
        page.clearCredentials(Number(key), true);
    });
};

// The tab's own address, as the browser reports it, for a frame of the tab's origin; null for a frame of another origin.
const pageUrlOf = function(tab, sender) {
    const tabOrigin = originOf(tab?.url);
    const frameOrigin = sender?.url ? originOf(sender.url) : tabOrigin;
    return tabOrigin && frameOrigin === tabOrigin ? tab.url : null;
};

// The names of the page's logins, the site taken from the browser's tab and never from the page.
page.listLogins = async function(tab, args = [], sender) {
    const url = pageUrlOf(tab, sender);
    if (!url) {
        return { state: 'frame' };
    }

    const [ submitUrl ] = args;
    const answer = await keepass.listLogins(tab, [ url, submitUrl ]);
    return { ...answer, site: answer.site || new URL(url).hostname };
};

// The one login the person picked in the list, for the tab's own address.
page.fillLogin = async function(tab, args = [], sender) {
    const url = pageUrlOf(tab, sender);
    if (!url) {
        return {};
    }

    const [ uuid, submitUrl ] = args;
    return await keepass.getLogin(tab, [ url, submitUrl, uuid ]);
};

page.openKeypaste = async function(tab, args = []) {
    return await keepass.openKeypaste(tab, args);
};

const originOf = function(url) {
    try {
        return new URL(url).origin;
    } catch {
        return '';
    }
};

page.getLoginId = async function(tab, returnSingle = true) {
    // If there's only one credential available and loginId is not set
    const currentTab = tabs.getTabFromId(tab.id);
    if (currentTab && returnSingle && !currentTab.loginId && currentTab.credentials.length === 1) {
        return currentTab.credentials[0].uuid;
    }

    return currentTab ? currentTab.loginId : undefined;
};

page.setLoginId = async function(tab, loginId) {
    tabs.updateTabValues(tab?.id, { loginId: loginId });
};

page.getManualFill = async function(tab) {
    return page.manualFill;
};

page.setManualFill = async function(tab, manualFill) {
    page.manualFill = manualFill;
};

page.getAutoSubmitPerformed = async function(tab) {
    return page.autoSubmitPerformed;
};

// Set autoSubmitPerformed to false after 5 seconds, preventing possible endless loops
page.setAutoSubmitPerformed = async function(tab) {
    if (!page.autoSubmitPerformed) {
        page.autoSubmitPerformed = true;

        setTimeout(() => {
            page.autoSubmitPerformed = false;
        }, AUTO_SUBMIT_TIMEOUT);
    }
};

// Returns login list for the extension popup
page.getLoginList = async function(tab) {
    return tabs.getTabFromId(tab.id)?.loginList ?? [];
};

page.isIframeAllowed = async function(tab, args = []) {
    try {
        const [ url ] = args;
        const currentFrameUrl = new URL(url);
        const tabUrl = new URL(tab?.url);
        return currentFrameUrl.origin === tabUrl.origin;
    } catch (e) {
        logError(e);
        return false;
    }
};

const logDebug = function(message, extra) {
    if (page.settings.debugLogging) {
        debugLogMessage(message, extra);
    }
};

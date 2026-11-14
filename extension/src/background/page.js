'use strict';

// keypaste decides these; the extension offers no settings of its own.
const defaultSettings = {
    afterFillSorting: SORT_BY_MATCHING_CREDENTIALS_SETTING,
    autoCompleteUsernames: true,
    autoFillSingleEntry: false,
    autoFillRelevantCredential: false,
    autoRetrieveCredentials: true,
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

// Retrieves the credentials. Returns cached values when found.
// Page reload or tab switch clears the cache.
page.retrieveCredentials = async function(tab, args = []) {
    if (!tab?.active || !tab?.id) {
        return [];
    }

    const [ url, submitUrl, force ] = args;
    const currentTab = tabs.getTabFromId(tab.id);
    if (currentTab?.credentials?.length > 0 && !force) {
        return currentTab.credentials;
    }

    // Ignore duplicate requests from the same tab
    if (page.currentRequest.url === url
        && page.currentRequest.submitUrl === submitUrl
        && page.currentRequest.tabId === tab.id
        && !force) {
        return [];
    } else {
        page.currentRequest.url = url;
        page.currentRequest.submitUrl = submitUrl;
        page.currentRequest.tabId = tab.id;
    }

    const credentials = await keepass.retrieveCredentials(tab, args);
    tabs.updateTabValues(tab.id, { credentials: credentials });
    return credentials;
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

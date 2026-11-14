'use strict';

const kpxcEvent = {};

kpxcEvent.onMessage = async function(request, sender) {
    if (request.action in kpxcEvent.messageHandlers) {
        if (!Object.hasOwn(sender, 'tab') || sender?.tab?.id < 1) {
            sender.tab = {};
            sender.tab.id = tabs.currentTabId;
        }

        return await kpxcEvent.messageHandlers[request.action](sender.tab, request.args);
    }
};

kpxcEvent.showStatus = async function(tab, configured, internalPoll, forceShowDefault = false) {
    let keyId = null;
    if (configured && keepass.databaseHash !== ''
        && Object.hasOwn(keepass.keyRing, keepass.databaseHash)) {
        keyId = keepass.keyRing[keepass.databaseHash].id;
    }

    if (!internalPoll || forceShowDefault) {
        browserAction.showDefault(tab);
    }

    const currentTab = tabs.getTabFromId(tab.id);
    const errorMessage = currentTab?.errorMessage ?? undefined;
    const iframeDetected = currentTab?.iframeDetected ?? false;

    return {
        associated: keepass.isAssociated(),
        configured: configured,
        databaseClosed: keepass.isDatabaseClosed,
        encryptionKeyUnrecognized: keepass.isEncryptionKeyUnrecognized,
        error: errorMessage,
        iframeDetected: iframeDetected,
        identifier: keyId,
        keypasteAvailable: keepass.isKeypasteAvailable
    };
};

kpxcEvent.onGetStatus = async function(tab, args = []) {
    // When internalPoll is true the event is triggered from content script in intervals -> don't poll keypaste
    try {
        const [ internalPoll = false, triggerUnlock = false, forceShowDefault ] = args;
        if (!internalPoll) {
            const response = await keepass.testAssociation(tab, [ true, triggerUnlock ]);
            if (!response) {
                return kpxcEvent.showStatus(tab, false);
            }
        }

        const configured = await keepass.isConfigured();
        return kpxcEvent.showStatus(tab, configured, internalPoll, forceShowDefault);
    } catch (err) {
        logError('No status shown: ' + err);
        return Promise.reject();
    }
};

kpxcEvent.onReconnect = async function(tab) {
    const configured = await keepass.reconnect(tab);
    if (configured) {
        browser.tabs.sendMessage(tab?.id, {
            action: 'redetect_fields'
        }).catch((err) => {
            logError(err);
            return;
        });
    }

    return kpxcEvent.showStatus(tab, configured);
};

kpxcEvent.lockDatabase = async function(tab) {
    try {
        await keepass.lockDatabase(tab);
        return kpxcEvent.showStatus(tab, false);
    } catch (err) {
        logError('kpxcEvent.lockDatabase error: ' + err);
        return false;
    }
};

kpxcEvent.onGetTabInformation = async function(tab) {
    const id = tab?.id || tabs.currentTabId;
    return tabs.getTabFromId(id);
};

kpxcEvent.onGetConnectedDatabase = async function() {
    return Promise.resolve({
        count: Object.keys(keepass.keyRing).length,
        identifier: (keepass.keyRing[keepass.associated.hash]) ? keepass.keyRing[keepass.associated.hash].id : null
    });
};

kpxcEvent.onLoginPopup = async function(tab, logins) {
    const popupData = {
        iconType: 'normal',
        popup: 'popup_login'
    };

    if (tab?.id) {
        tabs.updateTabValues(tab?.id, { loginList: logins });
        await browserAction.show(tab, popupData);
    }
};

kpxcEvent.onIframeDetected = async function(tab, detected) {
    tabs.updateTabValues(tab?.id, { iframeDetected: detected });
};

kpxcEvent.passwordGetFilled = async function() {
    return page.passwordFilled;
};

kpxcEvent.passwordSetFilled = async function(tab, state) {
    page.passwordFilled = state;
};

// The pairing the extension holds, for the options page
kpxcEvent.onGetPairing = async function() {
    return Object.values(keepass.keyRing)[0] ?? null;
};

kpxcEvent.pageClearLogins = async function(tab, alreadyCalled) {
    if (!alreadyCalled && tab?.id) {
        page.clearLogins(tab.id);
    }
};

kpxcEvent.getIsKeypasteAvailable = async function() {
    return keepass.isKeypasteAvailable;
};

// Bounce message back to all frames
kpxcEvent.sendBackToTabs = async function(tab, args = []) {
    if (tab?.id) {
        await browser.tabs.sendMessage(tab.id, { action: 'frame_message', args: args });
    }
};

// All methods named in this object have to be declared BEFORE this!
kpxcEvent.messageHandlers = {
    'associate': keepass.associate,
    'check_database_hash': keepass.checkDatabaseHash,
    'frame_message': kpxcEvent.sendBackToTabs,
    'get_connected_database': kpxcEvent.onGetConnectedDatabase,
    'get_database_hash': keepass.getDatabaseHash,
    'get_login_list': page.getLoginList,
    'get_pairing': kpxcEvent.onGetPairing,
    'get_status': kpxcEvent.onGetStatus,
    'get_tab_information': kpxcEvent.onGetTabInformation,
    'iframe_detected': kpxcEvent.onIframeDetected,
    'is_connected': kpxcEvent.getIsKeypasteAvailable,
    'is_iframe_allowed': page.isIframeAllowed,
    'load_settings': page.getSettings,
    'lock_database': kpxcEvent.lockDatabase,
    'page_clear_logins': kpxcEvent.pageClearLogins,
    'page_get_autosubmit_performed': page.getAutoSubmitPerformed,
    'page_get_login_id': page.getLoginId,
    'page_get_manual_fill': page.getManualFill,
    'page_set_autosubmit_performed': page.setAutoSubmitPerformed,
    'page_set_login_id': page.setLoginId,
    'page_set_manual_fill': page.setManualFill,
    'password_get_filled': kpxcEvent.passwordGetFilled,
    'password_set_filled': kpxcEvent.passwordSetFilled,
    'popup_login': kpxcEvent.onLoginPopup,
    'reconnect': kpxcEvent.onReconnect,
    'retrieve_credentials': page.retrieveCredentials,
    'show_default_browseraction': browserAction.showDefault
};

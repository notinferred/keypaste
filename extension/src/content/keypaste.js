'use strict';

// Contains already called method names
const _called = {};
_called.automaticRedetectCompleted = false;
_called.retrieveCredentials = false;

// Wrapper
const sendMessage = async function(action, args) {
    return await browser.runtime.sendMessage({ action: action, args: args });
};

/**
 * @Object kpxc
 * The main content script object.
 */
const kpxc = {};
kpxc.combinations = [];
kpxc.credentials = [];
kpxc.databaseState = DatabaseState.DISCONNECTED;
kpxc.detectedFields = 0;
kpxc.improvedFieldDetectionEnabledForPage = false;
kpxc.inputs = [];
kpxc.settings = {};
kpxc.singleInputEnabledForPage = false;
kpxc.submitUrl = null;
kpxc.url = null;

// Clears all from the content and background scripts, including autocomplete
kpxc.clearAllFromPage = function() {
    kpxc.credentials = [];
    kpxc.inputs = [];
    kpxcUserAutocomplete.clear();
    _called.retrieveCredentials = false;

    if (kpxc.settings.autoCompleteUsernames) {
        kpxcUserAutocomplete.closeList();
    }

    // Clear logins from background and switch back to default popup
    sendMessage('page_clear_logins');
    sendMessage('get_status', [ true, false, true ]); // This is an internal function call, forceShowDefault
};

// Creates a new combination manually from active element
kpxc.createCombination = async function(activeElement) {
    const combination = {
        username: null,
        password: null,
        passwordInputs: [],
        form: null
    };

    if (!activeElement) {
        return combination;
    }

    combination.form = activeElement.form;

    if (activeElement.getLowerCaseAttribute('type') === 'password') {
        combination.password = activeElement;
    } else {
        combination.username = activeElement;
    }

    return combination;
};

// Switch credentials if database is changed or closed
kpxc.detectDatabaseChange = async function(response) {
    kpxc.databaseState = DatabaseState.LOCKED;
    kpxc.clearAllFromPage();
    kpxcIcons.switchIcons();

    if (document.visibilityState !== 'hidden') {
        if (response.hash.new !== '') {
            _called.retrieveCredentials = false;
            const settings = await sendMessage('load_settings');
            kpxc.settings = settings;
            kpxc.databaseState = DatabaseState.UNLOCKED;

            await kpxc.initCredentialFields();
            kpxcIcons.switchIcons();

            // If user has requested a manual fill through context menu the actual credential filling
            // is handled here when the opened database has been regognized. It's not a pretty hack.
            const manualFill = await sendMessage('page_get_manual_fill');
            if (manualFill !== ManualFill.NONE && kpxc.combinations.length > 0) {
                await kpxcFill.fillInFromActiveElement();
                await sendMessage('page_set_manual_fill', ManualFill.NONE);
            }
        } else if (!response.connected) {
            kpxc.databaseState = DatabaseState.DISCONNECTED;
            kpxcIcons.switchIcons();
        }
    }
};

kpxc.getUniqueGroupCount = function(creds) {
    const groups = creds.map(c => c.group || '');
    const uniqueGroups = new Set(groups);
    return uniqueGroups.size;
};

// Returns the form that includes the inputField
kpxc.getForm = function(inputField) {
    if (inputField.form) {
        return inputField.form;
    }

    for (const f of document.forms) {
        for (const e of f.elements) {
            if (e === inputField) {
                return f;
            }
        }
    }
};

// Returns form action URL or document origin if it's not found
kpxc.getFormActionUrl = function(combination) {
    if (!combination || (combination.username === null && combination.password === null)) {
        return null;
    }

    let action = null;
    if (combination.form?.length > 0) {
        action = combination.form.action;
    }

    if (typeof(action) !== 'string' || action === '') {
        // Firefox can report location.origin as 'null' with localHost files
        const origin = document.location.origin === 'null' ? 'file://' : document.location.origin;
        action = origin + document.location.pathname;
    }

    return action;
};

// Identifies all forms in the page
kpxc.identifyFormInputs = async function() {
    const forms = [];
    const documentForms = document.forms; // Cache the value just in case
    // Used for overlay security detection
    kpxcFields.discoverOverlays();

    for (const form of documentForms) {
        if (!kpxcFields.isVisible(form)) {
            continue;
        }

        if (kpxcFields.isSearchForm(form)) {
            continue;
        }

        forms.push(form);
    }

    // Identify input fields in the saved forms
    const inputs = [];
    for (const form of forms) {
        const formInputs = kpxcObserverHelper.getInputs(form);
        for (const f of formInputs) {
            inputs.push(f);
        }
    }

    await kpxc.initCombinations(inputs);
    return inputs;
};

// Initialize autocomplete for username fields
kpxc.initAutocomplete = function() {
    if (!kpxc.settings.autoCompleteUsernames) {
        return;
    }

    const showGroupNameInAutocomplete = kpxc.showGroupNameInAutocomplete();

    for (const c of kpxc.combinations) {
        if (c.username) {
            kpxcUserAutocomplete.create(
                c.username,
                false,
                kpxc.settings.autoSubmit,
                kpxc.settings.useCompactMode,
                showGroupNameInAutocomplete,
                kpxc.settings.afterFillSorting,
            );
        } else if (!c.username && c.password) {
            // Single password field
            kpxcUserAutocomplete.create(
                c.password,
                false,
                kpxc.settings.autoSubmit,
                kpxc.settings.useCompactMode,
                showGroupNameInAutocomplete,
                kpxc.settings.afterFillSorting,
            );
        }

    }
};

// Looks for any username & password combinations from the detected input fields
kpxc.initCombinations = async function(inputs = []) {
    if (inputs.length === 0) {
        return [];
    }

    const combinations = await kpxcFields.getAllCombinations(inputs);
    if (!combinations || combinations.length === 0) {
        return [];
    }

    for (const c of combinations) {
        // Don't allow duplicates
        if (!kpxc.combinations.some(f =>
            f.username === c.username && f.password === c.password && f.form === c.form
        )) {
            kpxc.combinations.push(c);
        }
    }

    logDebug('Login field combinations identified:', combinations);
    return combinations;
};

// The main function for finding input fields
kpxc.initCredentialFields = async function() {
    // Identify all forms in the page
    const formInputs = await kpxc.identifyFormInputs();

    // Search all remaining inputs from the page, ignore the previous input fields
    const pageInputs = await kpxcFields.getAllPageInputs(formInputs);
    if (formInputs.length === 0 && pageInputs.length === 0) {
        // Run 'redetect_credentials' manually if no fields are found after a page load
        setTimeout(async function() {
            if (_called.automaticRedetectCompleted) {
                return;
            }

            if (kpxc.inputs.length === 0 || kpxc.combinations.length === 0) {
                kpxc.initCredentialFields();
            }
            _called.automaticRedetectCompleted = true;
        }, 2000);

        return;
    }

    // Combine inputs
    kpxc.inputs = [ ...formInputs, ...pageInputs ];

    // Combinations are already saved when identifying fields
    if (kpxc.combinations.length === 0) {
        sendMessage('show_default_browseraction');
        return;
    }

    await kpxcIcons.initIcons(kpxc.combinations);

    if (kpxc.databaseState === DatabaseState.UNLOCKED) {
        await kpxc.retrieveCredentials();
    }
};

// Initializes the login lists for popup and Autocomplete Menu
kpxc.initLoginPopup = function() {
    if (kpxc.credentials.length === 0) {
        return;
    }

    // Returns a login item with additional information for sorting
    const getLoginItem = function(credential, withGroup, loginId) {
        const title = credential.name.length < MAX_AUTOCOMPLETE_NAME_LEN
            ? credential.name
            : credential.name.substr(0, MAX_AUTOCOMPLETE_NAME_LEN) + '…';
        const group = (withGroup && credential.group) ? `[${credential.group}] ` : '';
        const visibleLogin = (credential.login.length > 0) ? credential.login : tr('credentialsNoUsername');
        let text = `${group}${title} (${visibleLogin})`;

        if (credential.expired && credential.expired === 'true') {
            text = `${text} [${tr('credentialExpired')}]`;
        }

        return {
            title: title,
            group: credential.group || '',
            visibleLogin: visibleLogin,
            login: credential.login,
            loginId: loginId,
            uuid: credential.uuid,
            expired: credential.expired === 'true',
            text: text
        };
    };

    // Sorting with or without group name included
    const sortLoginItemBy = function(a, b, name, withGroup = false) {
        const firstGroup = a.group?.toLowerCase();
        const secondGroup = b.group?.toLowerCase();
        const first = a[name]?.toLowerCase();
        const second = b[name]?.toLowerCase();

        return withGroup
            ? firstGroup.localeCompare(secondGroup) || first.localeCompare(second)
            : first.localeCompare(second);
    };

    const showGroupNameInAutocomplete = kpxc.showGroupNameInAutocomplete();

    // Initialize login items
    const loginItems = [];
    for (const [ i, login ] of kpxc.credentials.entries()) {
        const loginItem = getLoginItem(login, showGroupNameInAutocomplete, i);

        loginItems.push(loginItem);
    }

    // Sort login items
    if (kpxc.settings.credentialSorting === SORT_BY_TITLE) {
        loginItems.sort((a, b) => sortLoginItemBy(a, b, 'title'));
    } else if (kpxc.settings.credentialSorting === SORT_BY_USERNAME) {
        loginItems.sort((a, b) => sortLoginItemBy(a, b, 'visibleLogin'));
    } else if (kpxc.settings.credentialSorting === SORT_BY_GROUP_AND_TITLE) {
        loginItems.sort((a, b) => sortLoginItemBy(a, b, 'title', true));
    } else if (kpxc.settings.credentialSorting === SORT_BY_GROUP_AND_USERNAME) {
        loginItems.sort((a, b) => sortLoginItemBy(a, b, 'visibleLogin', true));
    }

    const popupLoginItems = [];
    kpxcUserAutocomplete.clear();

    // Initialize Popup Login and Autocomplete Menu items
    for (const l of loginItems) {
        popupLoginItems.push({ text: l.text, title: l.title, username: l.visibleLogin, expired: l.expired, uuid: l.uuid });

        kpxcUserAutocomplete.elements.push({
            group: showGroupNameInAutocomplete ? l.group : undefined,
            title: l.title,
            label: l.text,
            value: l.login,
            uuid: l.uuid,
            loginId: l.loginId
        });
    }

    // Activate Popup Login list of usernames + descriptions
    sendMessage('popup_login', popupLoginItems);
};

/**
 * Handle passwordFilled() with possible exceptions, e.g. Protonmail's mailbox password
 * where we actually need two passwords for a successful login.
 * If an exception is found, act like password is not yet filled.
 * @param {Object} currentForm  A login field combination
 * @returns {boolean}           True if password has been already filled
 */
kpxc.passwordFilledWithExceptions = async function(currentForm) {
    if (currentForm.password && kpxcSites.exceptionFound(currentForm.password.id)) {
        return false;
    }

    return await sendMessage('password_get_filled');
};

// Prepares autocomplete and login popup ready for user interaction
kpxc.prepareCredentials = async function() {
    if (kpxc.credentials.length === 0) {
        logDebug('Error: No combination found.');
        return;
    }

    if (kpxc.settings.autoFillSingleEntry && kpxc.credentials.length === 1) {
        kpxcFill.fillFromAutofill();
        return;
    }

    kpxc.initLoginPopup();
    kpxc.initAutocomplete();

    if (kpxc.settings.autoFillRelevantCredential) {
        const pageUuid = await sendMessage('page_get_login_id', false);
        if (pageUuid) {
            const relevantCredential = kpxc.credentials.find(c => c.uuid === pageUuid);
            const combination = kpxc.combinations?.at(-1);
            if (relevantCredential && combination) {
                kpxcFill.fillInCredentials(combination, relevantCredential.login, relevantCredential.uuid);
            }
        }
    }
};

// The basic function for retrieving credentials from keypaste.
kpxc.retrieveCredentials = async function(force = false) {
    if (!await isIframeAllowed()) {
        return;
    }

    kpxc.url = document.location.href;

    // Search for first combination that has username or password input set
    const firstCombination = kpxc.combinations?.find((combination) => combination?.username || combination?.password);
    kpxc.submitUrl = kpxc.getFormActionUrl(firstCombination);

    if (kpxc.settings.autoRetrieveCredentials && kpxc.url && kpxc.submitUrl) {
        await kpxc.retrieveCredentialsCallback(
            await sendMessage('retrieve_credentials', [ kpxc.url, kpxc.submitUrl, force ]),
        );
    }
};

// Handles credentials from 'retrieve_credentials' response
kpxc.retrieveCredentialsCallback = async function(credentials) {
    _called.retrieveCredentials = true;
    if (credentials?.length > 0) {
        kpxc.credentials = credentials;
        await kpxc.prepareCredentials();
    }
};

// If credentials are not received, request them again
kpxc.receiveCredentialsIfNecessary = async function() {
    if (kpxc.credentials.length === 0) {
        if (!await isIframeAllowed()) {
            return [];
        }

        if (!kpxc.url) {
            kpxc.url = document.location.href;
        }

        // Sets triggerUnlock to true
        const credentials = await sendMessage('retrieve_credentials', [ kpxc.url, kpxc.submitUrl, true ]);
        if (credentials.length === 0) {
            logDebug('Error: No credentials found.');
            return [];
        }

        // If the database was locked, this is scope never met.
        // In these cases the response is met at kpxc.detectDatabaseChange
        await sendMessage('page_set_manual_fill', ManualFill.NONE);
        await kpxc.retrieveCredentialsCallback(credentials);

        kpxcIcons.switchIcons();
        return credentials;
    }

    return kpxc.credentials;
};

kpxc.setPasswordFilled = async function(state) {
    await sendMessage('password_set_filled', state);
};

kpxc.showGroupNameInAutocomplete = function() {
    return !kpxc.settings.useCompactMode
        || (kpxc.settings.showGroupNameInAutocomplete && kpxc.getUniqueGroupCount(kpxc.credentials) > 1);
};

// Updates database status and icons when tab is activated again
kpxc.triggerActivatedTab = async function() {
    await kpxc.updateDatabaseState();

    if (kpxc.databaseState === DatabaseState.UNLOCKED && kpxc.credentials.length === 0) {
        await kpxc.retrieveCredentials();
    } else if (kpxc.credentials.length > 0) {
        kpxc.initLoginPopup();
    }

    kpxcIcons.switchIcons();
};

// Updates the database state to the content script
kpxc.updateDatabaseState = async function() {
    const res = await sendMessage('get_status', [ true ]);

    if (!res.keypasteAvailable) {
        kpxc.databaseState = DatabaseState.DISCONNECTED;
        return;
    }

    kpxc.databaseState = res.databaseClosed ? DatabaseState.LOCKED : DatabaseState.UNLOCKED;
};

// Enable certain settings based on predefined sites list
kpxc.usePredefinedSites = function() {
    if (!kpxc.settings.usePredefinedSites) {
        return;
    }

    let currentLocation;
    try {
        currentLocation = window.top.location.href;
    } catch (_err) {
        // A frame from another (sub)domain cannot read window.top.location, so it uses its own URL
        currentLocation = window.self.location.href;
    }

    // Single input field
    for (const url of PREDEFINED_SITELIST) {
        if (siteMatch(url, currentLocation) || url === currentLocation) {
            kpxc.singleInputEnabledForPage = true;
        }
    }

    // Improved input field detection
    for (const url of IMPROVED_DETECTION_PREDEFINED_SITELIST) {
        if (siteMatch(url, currentLocation) || url === currentLocation) {
            kpxc.improvedFieldDetectionEnabledForPage = true;
        }
    }
};

/**
 * Content script initialization.
 */
const initContentScript = async function() {
    try {
        if (document?.documentElement?.ownerDocument?.contentType !== 'text/html'
            && document?.documentElement?.ownerDocument?.contentType !== 'application/xhtml+xml'
        ) {
            return;
        }

        const settings = await sendMessage('load_settings');
        if (!settings) {
            logError('Error: Cannot load extension settings');
            return;
        }

        kpxc.settings = settings;
        kpxc.usePredefinedSites();

        await kpxc.updateDatabaseState();
        await kpxc.initCredentialFields();

        if (kpxc.settings.useObserver) {
            await kpxcObserverHelper.initObserver();
        }

        kpxcIcons.switchIcons();
    } catch (err) {
        logError('initContentScript error: ' + err);
    }
};

if (document.readyState === 'complete' || (document.readyState !== 'loading' && !document.documentElement.doScroll)) {
    initContentScript();
} else {
    document.addEventListener('DOMContentLoaded', initContentScript);
}

// These are executed in each frame
browser.runtime.onMessage.addListener(async function(req, sender) {
    if ('action' in req) {
        if (req.action === 'activated_tab') {
            kpxc.triggerActivatedTab();
        } else if (req.action === 'check_database_hash' && 'hash' in req) {
            kpxc.detectDatabaseChange(req);
        } else if (req.action === 'clear_credentials') {
            kpxc.clearAllFromPage();
        } else if (req.action === 'fill_user_pass_with_specific_login') {
            await kpxc.reconnect();
            kpxcFill.fillFromPopup(req.id, req.uuid);
        } else if (req.action === 'fill_username_password') {
            await kpxc.reconnect();
            sendMessage('page_set_manual_fill', ManualFill.BOTH);
            await kpxc.receiveCredentialsIfNecessary();
            kpxcFill.fillInFromActiveElement();
        } else if (req.action === 'frame_message') {
            if (req.args?.[0] === 'notification_from_frame' && window.self === window.top) {
                // Handle notification from iframe in top window
                kpxcUI.createNotification(req?.args[1], req?.args[2]);
            }
        } else if (req.action === 'redetect_fields') {
            const response = await sendMessage('load_settings');
            kpxc.settings = response;
            kpxc.inputs = [];
            kpxc.combinations = [];
            kpxc.initCredentialFields();
        } else if (req.action === 'retrieve_credentials_forced') {
            await kpxc.retrieveCredentials(true);
        }
    }
});

// Automatically reconnect to keypaste
// returns true if connected afterwards
kpxc.reconnect = async function() {
    // Try to reconnect if keypaste is not currently connected
    const connected = await sendMessage('is_connected');
    if (!connected) {
        const reconnectResponse = await sendMessage('reconnect');
        if (!reconnectResponse.keypasteAvailable) {
            kpxcUI.createNotification('error', tr('errorNotConnected'));
            return false;
        }
    }
    return true;
};

const isIframeAllowed = async function() {
    sendMessage('iframe_detected', false);

    // Don't allow sandboxed iframes
    if (self.origin === null || self.origin === 'null') {
        logDebug('Error: Sandboxed iframes are not allowed');
        return false;
    }

    const allowed = await sendMessage('is_iframe_allowed', [ window.location.href ]);
    if (allowed) {
        return true;
    }

    logDebug(`Error: Credential request ignored from another domain: ${window.self.location.host}`);
    sendMessage('iframe_detected', true);
    return false;
};

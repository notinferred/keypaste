'use strict';

const keepass = {};
keepass.associated = { 'value': false, 'hash': null };
keepass.clientID = '';
keepass.databaseHash = '';
keepass.isConnected = false;
keepass.isDatabaseClosed = true;
keepass.isEncryptionKeyUnrecognized = false;
keepass.isKeypasteAvailable = false;
keepass.keyPair = { publicKey: null, secretKey: null };
keepass.previousDatabaseHash = '';
keepass.serverPublicKey = '';

const kpActions = {
    GET_LOGINS: 'get-logins',
    ASSOCIATE: 'associate',
    TEST_ASSOCIATE: 'test-associate',
    GET_DATABASE_HASH: 'get-databasehash',
    CHANGE_PUBLIC_KEYS: 'change-public-keys',
    LOCK_DATABASE: 'lock-database',
    DATABASE_LOCKED: 'database-locked',
    DATABASE_UNLOCKED: 'database-unlocked'
};

browser.storage.local.get({ 'keyRing': {} }).then((item) => {
    keepass.keyRing = item.keyRing;
});

//--------------------------------------------------------------------------
// Commands
//--------------------------------------------------------------------------

keepass.retrieveCredentials = async function(tab, args = []) {
    try {
        const [ url, submiturl, triggerUnlock = false ] = args;
        const taResponse = await keepass.testAssociation(tab, [ false, triggerUnlock ]);
        if (!taResponse) {
            browserAction.showDefault(tab);
            return [];
        }

        keepass.clearErrorMessage(tab);

        if (!keepass.isConnected) {
            return [];
        }

        let entries = [];
        const kpAction = kpActions.GET_LOGINS;
        const nonce = keepassClient.getNonce();
        const [ dbid ] = keepass.getCryptoKey();

        const messageData = {
            action: kpAction,
            id: dbid,
            url: url,
            keys: keepass.getCryptoKeys()
        };

        if (submiturl) {
            messageData.submitUrl = submiturl;
        }

        const response = await keepassClient.sendMessage(kpAction, tab, messageData, nonce);
        if (response) {
            entries = removeDuplicateEntries(response.entries);
            keepass.updateLastUsed(keepass.databaseHash);

            if (entries.length === 0) {
                // Questionmark-icon is not triggered, so we have to trigger for the normal symbol
                browserAction.showDefault(tab);
            }

            logDebug(`Found ${entries.length} entries for url ${url}`);
            return entries;
        }

        browserAction.showDefault(tab);
        return [];
    } catch (err) {
        logError(`retrieveCredentials failed: ${err}`);
        return [];
    }
};

keepass.associate = async function(tab) {
    if (keepass.isAssociated()) {
        return AssociatedAction.ASSOCIATED;
    }

    try {
        await keepass.getDatabaseHash(tab);
        if (keepass.isDatabaseClosed || !keepass.isKeypasteAvailable) {
            return AssociatedAction.NOT_ASSOCIATED;
        }

        keepass.clearErrorMessage(tab);

        const kpAction = kpActions.ASSOCIATE;
        const key = nacl.util.encodeBase64(keepass.keyPair.publicKey);
        const nonce = keepassClient.getNonce();
        const idKeyPair = nacl.box.keyPair();
        const idKey = nacl.util.encodeBase64(idKeyPair.publicKey);

        const messageData = {
            action: kpAction,
            key: key,
            idKey: idKey
        };

        const response = await keepassClient.sendMessage(kpAction, tab, messageData, nonce, false, true);
        if (response) {
            keepass.setCryptoKey(response.id, idKey);
            keepass.associated.value = true;
            keepass.associated.hash = response.hash || 0;

            browserAction.showDefault(tab);
            return AssociatedAction.NEW_ASSOCIATION;
        }

        keepass.handleError(tab, kpErrors.ASSOCIATION_FAILED);
        return AssociatedAction.NOT_ASSOCIATED;
    } catch (err) {
        logError(`associate failed: ${err}`);
    }

    return AssociatedAction.NOT_ASSOCIATED;
};

keepass.testAssociation = async function(tab, args = []) {
    keepass.clearErrorMessage(tab);

    try {
        const [ enableTimeout = false, triggerUnlock = false ] = args;
        const dbHash = await keepass.getDatabaseHash(tab, [ enableTimeout, triggerUnlock ]);
        if (!dbHash) {
            return false;
        }

        if (keepass.isDatabaseClosed || !keepass.isKeypasteAvailable) {
            return false;
        }

        if (!keepass.serverPublicKey) {
            if (tab && tabs.getTabFromId(tab.id)) {
                keepass.handleError(tab, kpErrors.PUBLIC_KEY_NOT_FOUND);
            }
            return false;
        }

        const kpAction = kpActions.TEST_ASSOCIATE;
        const nonce = keepassClient.getNonce();
        const [ dbid, dbkey ] = keepass.getCryptoKey();

        if (dbkey === null || dbid === null) {
            if (tab && tabs.getTabFromId(tab.id)) {
                keepass.handleError(tab, kpErrors.NO_SAVED_DATABASES_FOUND);
            }
            return false;
        }

        const messageData = {
            action: kpAction,
            id: dbid,
            key: dbkey
        };

        const response = await keepassClient.sendMessage(kpAction, tab, messageData, nonce, enableTimeout);
        if (!response) {
            const hash = response.hash || 0;
            keepass.deleteKey(hash);
            keepass.isEncryptionKeyUnrecognized = true;
            keepass.handleError(tab, kpErrors.ENCRYPTION_KEY_UNRECOGNIZED);
            keepass.associated.value = false;
            keepass.associated.hash = null;
        } else if (!keepass.isAssociated()) {
            keepass.handleError(tab, kpErrors.ASSOCIATION_FAILED);
        } else {
            keepass.isEncryptionKeyUnrecognized = false;
            keepass.clearErrorMessage(tab);
        }

        return keepass.isAssociated();
    } catch (err) {
        logError(`testAssociation failed: ${err}`);
        return false;
    }
};

keepass.getDatabaseHash = async function(tab, args = []) {
    if (!keepass.isConnected) {
        keepass.handleError(tab, kpErrors.TIMEOUT_OR_NOT_CONNECTED);
        return '';
    }

    if (!keepass.serverPublicKey) {
        keepass.changePublicKeys(tab);
    }

    const [ enableTimeout = false, triggerUnlock = false ] = args;
    const kpAction = kpActions.GET_DATABASE_HASH;
    const [ nonce, incrementedNonce ] = keepassClient.getNonces();

    const messageData = {
        action: kpAction,
        connectedKeys: Object.keys(keepass.keyRing) // This will be removed in the future
    };

    const encrypted = keepassClient.encrypt(messageData, nonce);
    if (encrypted.length <= 0) {
        keepass.handleError(tab, kpErrors.PUBLIC_KEY_NOT_FOUND);
        keepass.updateDatabaseHashToContent();
        return keepass.databaseHash;
    }

    try {
        const request = keepassClient.buildRequest(
            kpAction,
            keepassClient.encrypt(messageData, nonce),
            nonce,
            keepass.clientID,
            triggerUnlock,
        );
        const response = await keepassClient.sendNativeMessage(request, enableTimeout);
        if (response.message && response.nonce) {
            const res = keepassClient.decrypt(response.message, response.nonce);
            if (!res) {
                keepass.handleError(tab, kpErrors.CANNOT_DECRYPT_MESSAGE);
                return '';
            }

            const message = nacl.util.encodeUTF8(res);
            const parsed = JSON.parse(message);
            if (keepassClient.verifyDatabaseResponse(parsed, incrementedNonce) && parsed.hash) {
                const oldDatabaseHash = keepass.databaseHash;
                keepass.databaseHash = parsed.hash || '';

                if (oldDatabaseHash && oldDatabaseHash !== keepass.databaseHash) {
                    keepass.associated.value = false;
                    keepass.associated.hash = null;
                }

                keepass.isDatabaseClosed = false;
                keepass.isKeypasteAvailable = true;

                // Update the databaseHash from legacy hash
                if (parsed.oldHash) {
                    keepass.updateDatabaseHash(parsed.oldHash, parsed.hash);
                }

                return parsed.hash;
            } else if (parsed.errorCode) {
                keepass.databaseHash = '';
                keepass.isDatabaseClosed = true;
                keepass.handleError(tab, kpErrors.DATABASE_NOT_OPENED);
                return keepass.databaseHash;
            }

            return keepass.databaseHash;
        }

        keepass.databaseHash = '';
        keepass.isDatabaseClosed = true;
        if ((response.message && response.message === '') || response.errorCode === kpErrors.TIMEOUT_OR_NOT_CONNECTED) {
            keepass.isKeypasteAvailable = false;
            keepass.isConnected = false;
            keepass.handleError(tab, kpErrors.TIMEOUT_OR_NOT_CONNECTED);
        } else {
            keepass.handleError(tab, response.errorCode, response.error);
        }
        return keepass.databaseHash;
    } catch (err) {
        logError(`getDatabaseHash failed: ${err}`);
        return keepass.databaseHash;
    }
};

keepass.changePublicKeys = async function(tab, enableTimeout = false, connectionTimeout) {
    if (!keepass.isConnected) {
        keepass.handleError(tab, kpErrors.TIMEOUT_OR_NOT_CONNECTED);
        return false;
    }

    const kpAction = kpActions.CHANGE_PUBLIC_KEYS;
    const key = nacl.util.encodeBase64(keepass.keyPair.publicKey);
    const [ nonce, incrementedNonce ] = keepassClient.getNonces();
    keepass.clientID = nacl.util.encodeBase64(nacl.randomBytes(keepassClient.keySize));

    const request = {
        action: kpAction,
        publicKey: key,
        nonce: nonce,
        clientID: keepass.clientID
    };

    try {
        const response = await keepassClient.sendNativeMessage(request, enableTimeout, connectionTimeout);

        if (!keepassClient.verifyKeyResponse(response, key, incrementedNonce)) {
            if (tab && tabs.getTabFromId(tab.id)) {
                keepass.handleError(tab, kpErrors.KEY_CHANGE_FAILED);
            }

            keepass.updateDatabaseHashToContent();
            return false;
        }

        keepass.isKeypasteAvailable = true;
        console.log(`${EXTENSION_NAME}: Server public key: ${nacl.util.encodeBase64(keepass.serverPublicKey)}`);
        return true;
    } catch (err) {
        logError(`changePublicKeys failed: ${err}`);
        return false;
    }
};

keepass.lockDatabase = async function(tab) {
    if (!keepass.isConnected) {
        keepass.handleError(tab, kpErrors.TIMEOUT_OR_NOT_CONNECTED);
        return false;
    }

    const kpAction = kpActions.LOCK_DATABASE;
    const nonce = keepassClient.getNonce();

    const messageData = {
        action: kpAction
    };

    try {
        const response = await keepassClient.sendMessage(kpAction, tab, messageData, nonce);
        if (response) {
            keepass.isDatabaseClosed = true;
            keepass.updateDatabase();

            // Display error message in the popup
            keepass.handleError(tab, kpErrors.DATABASE_NOT_OPENED);
            return true;
        } else {
            keepass.isDatabaseClosed = true;
        }

        return false;
    } catch (err) {
        logError(`ockDatabase failed: ${err}`);
        return false;
    }
};

//--------------------------------------------------------------------------
// Keyring
//--------------------------------------------------------------------------

keepass.migrateKeyRing = function() {
    return new Promise((resolve, reject) => {
        browser.storage.local.get('keyRing').then((item) => {
            const keyring = item.keyRing;
            // Change dates to numbers, for compatibility with Chromium based browsers
            if (keyring) {
                let num = 0;
                for (const keyHash in keyring) {
                    const key = keyring[keyHash];
                    [ 'created', 'lastUsed' ].forEach((fld) => {
                        const v = key[fld];
                        if (v instanceof Date && v.valueOf() >= 0) {
                            key[fld] = v.valueOf();
                            num++;
                        } else if (typeof v !== 'number') {
                            key[fld] = Date.now().valueOf();
                            num++;
                        }
                    });
                }
                if (num > 0) {
                    browser.storage.local.set({ keyRing: keyring });
                }
            }
            resolve();
        });
    });
};

// The extension holds one pairing: a new one replaces any other.
keepass.saveKey = function(hash, id, key) {
    keepass.keyRing = {
        [hash]: {
            id: id,
            key: key,
            hash: hash,
            created: new Date().valueOf(),
            lastUsed: new Date().valueOf()
        }
    };

    browser.storage.local.set({ 'keyRing': keepass.keyRing });
};

keepass.updateLastUsed = function(hash) {
    if (Object.hasOwn(keepass.keyRing, hash)) {
        keepass.keyRing[hash].lastUsed = new Date().valueOf();
        browser.storage.local.set({ 'keyRing': keepass.keyRing });
    }
};

// Update the databaseHash from legacy hash
keepass.updateDatabaseHash = function(oldHash, newHash) {
    if (!oldHash || !newHash || oldHash === newHash) {
        return;
    }

    if ((oldHash in keepass.keyRing)) {
        keepass.keyRing[newHash] = keepass.keyRing[oldHash];
        keepass.keyRing[newHash].hash = newHash;
        delete keepass.keyRing[oldHash];
        browser.storage.local.set({ 'keyRing': keepass.keyRing });
    }
};

keepass.deleteKey = function(hash) {
    delete keepass.keyRing[hash];
    browser.storage.local.set({ 'keyRing': keepass.keyRing });
};

keepass.getCryptoKey = function() {
    let dbkey = null;
    let dbid = null;

    if (!(keepass.databaseHash in keepass.keyRing)) {
        return [ dbid, dbkey ];
    }

    dbid = keepass.keyRing[keepass.databaseHash].id;

    if (dbid) {
        dbkey = keepass.keyRing[keepass.databaseHash].key;
    }

    return [ dbid, dbkey ];
};

keepass.setCryptoKey = function(id, key) {
    keepass.saveKey(keepass.databaseHash, id, key);
};

keepass.getCryptoKeys = function() {
    const keys = [];

    for (const keyHash in keepass.keyRing) {
        keys.push({
            id: keepass.keyRing[keyHash].id,
            key: keepass.keyRing[keyHash].key
        });
    }

    return keys;
};

//--------------------------------------------------------------------------
// Connection
//--------------------------------------------------------------------------

keepass.reconnect = async function(tab = null, connectionTimeout = 1500) {
    keepassClient.connectToNative();

    keepass.generateNewKeyPair();
    const keyChangeResult = await keepass
        .changePublicKeys(tab, !!connectionTimeout, connectionTimeout)
        .catch(() => false);

    // Change public keys timeout
    if (!keyChangeResult) {
        return false;
    }

    const hash = await keepass.getDatabaseHash(tab);
    if (hash !== '') {
        keepass.clearErrorMessage(tab);
    }

    await keepass.testAssociation();
    await keepass.isConfigured();
    keepass.updateDatabaseHashToContent();
    return true;
};

//--------------------------------------------------------------------------
// Utils
//--------------------------------------------------------------------------

keepass.generateNewKeyPair = function() {
    keepass.keyPair = nacl.box.keyPair();
};

keepass.isConfigured = async function() {
    if (typeof(keepass.databaseHash) === 'undefined') {
        const hash = keepass.getDatabaseHash();
        return Object.hasOwn(keepass.keyRing, hash);
    }

    return keepass.databaseHash in keepass.keyRing;
};

keepass.checkDatabaseHash = async function(tab) {
    return keepass.databaseHash;
};

keepass.isAssociated = function() {
    return (keepass.associated.value && keepass.associated.hash && keepass.associated.hash === keepass.databaseHash);
};

keepass.clearErrorMessage = function(tab) {
    tabs.updateTabValues(tab?.id, { errorMessage: undefined });
};

keepass.handleError = function(tab, errorCode, errorMessage = '') {
    if (errorMessage.length === 0) {
        errorMessage = kpErrors.getError(errorCode);
    }

    logError(`${errorCode}: ${errorMessage}`);
    tabs.updateTabValues(tab?.id, { errorMessage: errorMessage });
};

keepass.updatePopup = function() {
    if (page && tabs.tabList.length > 0) {
        browserAction.showDefault();
    }
};

// Updates the database hashes to content script
keepass.updateDatabase = async function() {
    keepass.associated.value = false;
    keepass.associated.hash = null;
    page.clearAllLogins();

    await keepass.testAssociation(null, [ true ]);

    keepass.updatePopup();
    keepass.updateDatabaseHashToContent();
};

keepass.updateDatabaseHashToContent = async function() {
    try {
        // Get all active tabs from all windows
        const currentWindowTabs = await browser.tabs.query({ active: true, currentWindow: true, discarded: false });
        const otherTabs = await browser.tabs.query({ active: true, currentWindow: false, discarded: false });
        const allTabs = [ ...currentWindowTabs, ...otherTabs ];

        for (const tab of allTabs) {
            if (tab?.id) {
                // Send message to content script
                browser.tabs.sendMessage(tab.id, {
                    action: 'check_database_hash',
                    hash: { old: keepass.previousDatabaseHash, new: keepass.databaseHash },
                    connected: keepass.isKeypasteAvailable
                }).catch((err) => {
                    logError('No content script available for this tab.');
                });
            }
        }

        keepass.previousDatabaseHash = keepass.databaseHash;
    } catch (err) {
        logError(`updateDatabaseHashToContent failed: ${err}`);
    }
};

const removeDuplicateEntries = function(arr) {
    const newArray = [];

    for (const a of arr) {
        if (newArray.some(i => i.uuid === a.uuid)) {
            continue;
        }

        newArray.push(a);
    }

    return newArray;
};

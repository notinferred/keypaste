'use strict';

/**
 * @Object kpxcFill
 * The class for filling credentials.
 */
const kpxcFill = {};

// Fill requested from the context menu. Active element is used for combination detection
kpxcFill.fillInFromActiveElement = async function() {
    const elem = document.activeElement;

    await kpxc.receiveCredentialsIfNecessary();
    if (kpxc.credentials.length === 0) {
        logDebug(`Error: Credential list is empty for: ${document.location.origin}`);
        showErrorNotification(`${tr('credentialsNoLoginsFound')} ${document.location.origin}`);
        return;
    }

    if (kpxc.combinations.length > 0) {
        if (await kpxcFill.fillFromCombination(elem)) {
            // Combination found and filled
            return;
        }
    }

    // No previous combinations detected. Create a new one from active element
    const combination = await kpxc.createCombination(elem);

    await sendMessage('page_set_login_id', kpxc.credentials[0].uuid);
    kpxcFill.fillInCredentials(combination, kpxc.credentials[0].login, kpxc.credentials[0].uuid);
};

// Fill from combination, if found
kpxcFill.fillFromCombination = async function(elem) {
    const combination = kpxc.combinations.find(c => c.username === elem) ?? kpxc.combinations.find(c => c.username);
    if (!combination) {
        logDebug('Error: No username/password field combination found.');
        return false;
    }

    const field = combination.username;
    if (!field) {
        logDebug('Error: No input field found.');
        return false;
    }

    // Set focus to the input field
    field.focus();

    if (kpxc.credentials.length > 1 && kpxc.settings.autoCompleteUsernames) {
        // More than one credential -> show autocomplete list
        kpxcUserAutocomplete.showList(field);
    } else {
        // Just one credential -> fill the first combination found
        await sendMessage('page_set_login_id', kpxc.credentials[0].uuid);
        kpxcFill.fillInCredentials(combination, kpxc.credentials[0].login, kpxc.credentials[0].uuid);
    }

    return true;
};

// Fill requested by Auto-Fill
kpxcFill.fillFromAutofill = async function() {
    if (kpxc.credentials.length !== 1 || kpxc.combinations.length === 0) {
        logDebug('Error: Credential list is empty or contains more than one entry.');
        return;
    }

    const index = kpxc.combinations.length - 1;
    await sendMessage('page_set_login_id', kpxc.credentials[0].uuid);
    kpxcFill.fillInCredentials(kpxc.combinations[index], kpxc.credentials[0].login, kpxc.credentials[0].uuid);

    // Generate popup-list of usernames + descriptions
    sendMessage('popup_login', [
        {
            text: `${kpxc.credentials[0].login} (${kpxc.credentials[0].name})`,
            title: kpxc.credentials[0].name,
            username: kpxc.credentials[0].login || tr('credentialsNoUsername'),
            uuid: kpxc.credentials[0].uuid
        },
    ]);
};

// Fill requested by selecting credentials from the popup
kpxcFill.fillFromPopup = async function(id, uuid) {
    if (kpxc.credentials.length === 0 || !kpxc.credentials[id] || kpxc.combinations.length === 0) {
        logDebug('Error: Credential list is empty.');
        return;
    }

    await sendMessage('page_set_login_id', uuid);
    const selectedCredentials = kpxc.credentials.find(c => c.uuid === uuid);
    if (!selectedCredentials) {
        logError('Uuid not found: ', uuid);
        return;
    }

    const foundCombination = kpxcFields.getCombinationFromAllInputs();
    kpxcFill.fillInCredentials(foundCombination, selectedCredentials.login, uuid);
    kpxcUserAutocomplete.closeList();
};

// Fill requested from username icon
kpxcFill.fillFromUsernameIcon = async function(combination) {
    await kpxc.receiveCredentialsIfNecessary();
    if (kpxc.credentials.length === 0) {
        logDebug(`Error: Credential list is empty for: ${document.location.origin}`);
        showErrorNotification(`${tr('credentialsNoLoginsFound')} ${document.location.origin}`);
        return;
    } else if (kpxc.credentials.length > 1 && kpxc.settings.autoCompleteUsernames) {
        kpxcUserAutocomplete.showList(combination.username || combination.password);
        return;
    }

    await sendMessage('page_set_login_id', kpxc.credentials[0].uuid);
    kpxcFill.fillInCredentials(combination, kpxc.credentials[0].login, kpxc.credentials[0].uuid);
};

/**
 * The main function for filling any credentials
 * @param {Array} combination Combination to be used
 * @param {String} predefinedUsername Predefined username. If set, there's no need to find it from combinations
 * @param {String} uuid Identifier for the entry. There can be identical usernames with different password
 */
kpxcFill.fillInCredentials = async function(combination, predefinedUsername, uuid) {
    if (kpxc.credentials.length === 0) {
        showErrorNotification(`${tr('credentialsNoLoginsFound')} ${document.location.origin}`);
        return;
    }

    if (!combination) {
        logDebug('Error: Empty login combination.');
        return;
    }

    // Use predefined username as default
    let usernameValue = predefinedUsername;

    // With single password field the combination.password is used instead
    usernameValue ??= combination.username ? combination.username.value : combination.password.value;

    // Find the correct credentials
    const selectedCredentials = kpxc.credentials.find(c => c.uuid === uuid);
    if (!selectedCredentials) {
        logError('Uuid not found: ' + uuid);
        return;
    }

    // Handle auto-submit
    let skipAutoSubmit = false;
    if (selectedCredentials.skipAutoSubmit !== undefined) {
        skipAutoSubmit = selectedCredentials.skipAutoSubmit === 'true';
    }

    // Additional checks
    checkIdenticalPasswordFields(combination);
    checkReadOnlyUsernameField(combination);

    // Fill password
    if (combination.password && matchesWithNodeName(combination.password, 'INPUT')) {
        // Show a notification if password length exceeds the length defined in input
        if (combination.password.maxLength
            && combination.password.maxLength > 0
            && selectedCredentials.password.length > combination.password.maxLength) {
            kpxcUI.createNotification('warning', tr('errorMessagePaswordLengthExceeded'));
        }

        await kpxcFill.setValueWithChange(combination.password, selectedCredentials.password);
        await kpxc.setPasswordFilled(true);
    }

    // Fill username
    if (combination.username && usernameValue && combination.username !== combination.password &&
        (!combination.username.value || combination.username.value !== usernameValue)) {
        await kpxcFill.setValueWithChange(combination.username, usernameValue);
    }

    // Close autocomplete menu after fill
    kpxcUserAutocomplete.closeList();

    // Reset ManualFill
    await sendMessage('page_set_manual_fill', ManualFill.NONE);

    await kpxcFill.performAutoSubmit(combination, skipAutoSubmit);
};

// Performs Auto-Submit. If filling single credentials is enabled, a 5 second timeout will be needed for fill
kpxcFill.performAutoSubmit = async function(combination, skipAutoSubmit) {
    if (!kpxc.settings.autoSubmit) {
        return;
    }

    const isAutoSubmitPerformed = await sendMessage('page_get_autosubmit_performed');
    if (isAutoSubmitPerformed && kpxc.settings.autoFillSingleEntry) {
        return;
    }

    if (!skipAutoSubmit) {
        await sendMessage('page_set_autosubmit_performed');

        const submitButton = kpxcForm.getFormSubmitButton(combination.form);
        if (submitButton !== undefined) {
            submitButton.click();
        } else if (combination.form) {
            combination.form.submit();
        }
    } else {
        (combination.username || combination.password).focus();
    }
};

// Sets a new value to input field and triggers necessary events
kpxcFill.setValueWithChange = async function(field, value) {
    if (!field || field?.readOnly) {
        return;
    }

    // Check for overlays before fill
    kpxcFields.discoverOverlays();
    const rect = field.getBoundingClientRect();
    if (kpxcFields.isOverlayOnTop(rect)) {
        return;
    }

    const dispatchLegacyEvent = function(elem, eventName) {
        const legacyEvent = elem.ownerDocument.createEvent('Event');
        legacyEvent.initEvent(eventName, true, false);
        elem.dispatchEvent(legacyEvent);
    };

    field.focus();

    // Use a delay to allow focus events to trigger and to give some
    // breathing room to frameworks that rely on promises to update their
    // state, like React. Not doing so can break OTP input, see issue #2215.
    await Promise.resolve();

    field.dispatchEvent(new FocusEvent('focus', { bubbles: false, cancelable: false }));
    field.dispatchEvent(new FocusEvent('focusin', { bubbles: true, cancelable: false }));

    // https://w3c.github.io/uievents/#keypress-event-order
    field.dispatchEvent(new KeyboardEvent('keydown', { bubbles: true, cancelable: false, key: value }));
    field.dispatchEvent(new InputEvent('beforeinput', { bubbles: true, cancelable: false, inputType: 'insertText', data: value }));
    field.dispatchEvent(new KeyboardEvent('keypress', { bubbles: true, cancelable: false, key: value }));
    field.value = value;
    field.dispatchEvent(new Event('input', { bubbles: true, cancelable: false }));
    field.dispatchEvent(new KeyboardEvent('keyup', { bubbles: true, cancelable: false, key: value }));
    field.dispatchEvent(new Event('change', { bubbles: true, cancelable: false }));

    // Some pages will not accept the value change without dispatching events directly to the document
    dispatchLegacyEvent(field, 'input');
    dispatchLegacyEvent(field, 'change');
};


// Show a specific error notification if current database is not connected
const showErrorNotification = async function(errorMessage, notificationType = 'error') {
    const connectedDatabase = await sendMessage('get_connected_database');
    if (!connectedDatabase?.identifier && kpxc.databaseState === DatabaseState.UNLOCKED) {
        kpxcUI.createNotification('error', tr('errorCurrentDatabaseNotConnected'));
    } else if (!await isIframeAllowed()) {
        // Special error if we are blocking due to iframe
        kpxcUI.createNotification('error', tr('credentialsBlockedInIframe'));
    } else {
        kpxcUI.createNotification(notificationType, errorMessage);
    }
};

// Update the password field if form has been updated with a new identical element (Moodle).
// If found, replace the new input field to the combination.
const checkIdenticalPasswordFields = function(combination) {
    if (combination?.form && combination?.password && !combination.form.contains(combination.password)) {
        const newPasswordField = getPasswordFieldFromForm(combination);
        if (newPasswordField && areNamedNodeMapsEqual(combination.password.attributes, newPasswordField.attributes)) {
            combination.password = newPasswordField;
        }
    }
};

// Sometimes username field is changed to readOnly. Look for a password field instead inside the form.
const checkReadOnlyUsernameField = function(combination) {
    if (combination?.username && combination?.username?.readOnly && combination?.form && !combination?.password) {
        const passwordField = getPasswordFieldFromForm(combination);
        if (kpxcFields.isVisible(passwordField)) {
            combination.password = passwordField;
        }
    }
};

// Checks if two NamedNodeMaps (attribute lists) are equal
const areNamedNodeMapsEqual = function(currentNodeMap, newNodeMap) {
    if (!currentNodeMap || !newNodeMap) {
        return false;
    }

    const fieldAttributes = Array.from(currentNodeMap);
    const newFieldAttributes = Array.from(newNodeMap);

    if (fieldAttributes.length !== newFieldAttributes.length) {
        return false;
    }

    for (const attr of fieldAttributes) {
        const newAttr = newFieldAttributes.find((newAttr) => newAttr?.name === attr?.name);
        if (!newAttr || newAttr?.value !== attr?.value) {
            return false;
        }
    }

    return true;
};

const getPasswordFieldFromForm = function(combination) {
    if (!combination) {
        return undefined;
    }

    const formInputs = kpxcObserverHelper.getInputs(combination.form);
    return formInputs?.find(
        formInput => formInput?.getLowerCaseAttribute('type') === 'password' && !formInput?.readOnly
    );
};

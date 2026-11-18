'use strict';

/**
 * @Object kpxcFill
 * The class for filling credentials.
 */
const kpxcFill = {};

// The one login the person picked, asked of keypaste for the tab's address and filled into the field's form; nothing is kept.
kpxcFill.fillPicked = async function(combination, uuid) {
    if (!combination || !uuid) {
        return;
    }

    const login = await sendMessage('fill_login', [ uuid, kpxc.submitUrl ]);
    if (login?.uuid !== uuid) {
        kpxcUI.createNotification('warning', tr(login?.locked ? 'loginListKeypasteLocked' : 'loginListNotFilled'));
        return;
    }

    await sendMessage('page_set_login_id', uuid);
    await kpxcFill.fillLogin(combination, login);
};

// Picked in the toolbar's login popup, the extension's own page
kpxcFill.fillFromPopup = async function(uuid) {
    if (kpxc.combinations.length === 0) {
        logDebug('Error: No login fields on this page.');
        return;
    }

    await kpxcFill.fillPicked(kpxcFields.getCombinationFromAllInputs(), uuid);
};

/**
 * Fills one released login into a form
 * @param {Object} combination  The form's username and password fields
 * @param {Object} login        The login keypaste released: login, password, name and uuid
 */
kpxcFill.fillLogin = async function(combination, login) {
    const usernameValue = login.login;

    // Additional checks
    checkIdenticalPasswordFields(combination);
    checkReadOnlyUsernameField(combination);

    if (combination.password && matchesWithNodeName(combination.password, 'INPUT')) {
        // Show a notification if password length exceeds the length defined in input
        if (combination.password.maxLength
            && combination.password.maxLength > 0
            && login.password.length > combination.password.maxLength) {
            kpxcUI.createNotification('warning', tr('errorMessagePaswordLengthExceeded'));
        }

        await kpxcFill.setValueWithChange(combination.password, login.password);
        await kpxc.setPasswordFilled(true);
    }

    if (combination.username && usernameValue && combination.username !== combination.password &&
        (!combination.username.value || combination.username.value !== usernameValue)) {
        await kpxcFill.setValueWithChange(combination.username, usernameValue);
    }

    kpxcUserAutocomplete.closeList();
    await kpxcFill.performAutoSubmit(combination, false);
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

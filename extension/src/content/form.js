'use strict';

/**
 * @Object kpxcForm
 * Finds a form's submit button for Auto-Submit.
 */
const kpxcForm = {};
kpxcForm.formButtonQuery = 'button[type=button], button[type=submit], input[type=button], input[type=submit], button:not([type]), div[role=button]';

// Get the form submit button instead if action URL is same as the page itself
kpxcForm.getFormSubmitButton = function(form) {
    if (!form || !form.action || typeof form.action !== 'string') {
        return;
    }

    const hasPasswordClassOrId = (button) => button
        && (button?.classList.value?.toLowerCase()?.includes('password')
            || button?.id?.toLowerCase()?.includes('password'));
    const action = kpxc.submitUrl || form.action;

    // Check if the site needs a special handling for retrieving the form submit button
    const exceptionButton = kpxcSites.formSubmitButtonExceptionFound(form);
    if (exceptionButton) {
        return exceptionButton;
    }

    if (action.includes(document.location.origin + document.location.pathname)) {
        for (const i of form.elements) {
            if (i.type === 'submit') {
                return i;
            }
        }
    }

    // Try to find another button. Select the last one.
    // If any formaction overriding the default action is set, ignore those buttons.
    const buttons = Array.from(form.querySelectorAll(kpxcForm.formButtonQuery)).filter(
        b => !b.getAttribute('formAction')
    );
    if (buttons.length > 0) {
        const lastButton = buttons.at(-1);
        // Accept button if it has no indication for password
        if (!hasPasswordClassOrId(lastButton)) {
            return buttons.at(-1);
        }
    }

    // Try to find similar buttons outside the form which are added via 'form' property
    for (const e of form.elements) {
        const isSubmitButton = matchesWithNodeName(e, 'BUTTON')
            && (e.type === 'button' || e.type === 'submit' || e.type === '');
        const isInputButton = matchesWithNodeName(e, 'INPUT') && (e.type === 'button' || e.type === 'submit');
        if ((isSubmitButton || isInputButton) && !hasPasswordClassOrId(e)) {
            return e;
        }
    }

    logDebug('No form submit button found.');
    return undefined;
};

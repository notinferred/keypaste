'use strict';

function kpxcAssert(func, expected, card, testName) {
    if (func === expected) {
        createResult(card, true, `Test passed: ${testName}`);
        return;
    }

    createResult(card, false, `Test failed: ${testName}. Result is: ${func}`);
}

function assertRegex(res, expected, card, testName) {
    if ((res === null && expected === false)
        || (res && (res.length > 0) === expected)
        || (res === expected)) {
        createResult(card, true, `Test passed: ${testName}`);
        return;
    }

    createResult(card, false, `Test failed: ${testName}. Result is: ${res}`);
}

async function assertInputFields(localDiv, expectedFieldCount, actionElementId) {
    const div = document.getElementById(localDiv);
    div.style.display = 'block';

    // An user interaction is required before testing
    if (actionElementId) {
        const actionElement = div.querySelector(actionElementId);
        if (actionElement) {
            actionElement.click();
        }
    }

    const inputs = kpxcObserverHelper.getInputs(div);
    kpxcAssert(inputs.length, expectedFieldCount, Tests.INPUT_FIELDS, `getInputs() for ${localDiv} with ${expectedFieldCount} fields`);

    div.style.display = 'none';
}

async function assertSearchField(classStr, properties, testName, expectedResult) {
    const input = kpxcUI.createElement('input', classStr, properties);
    document.body.appendChild(input);

    const isSearchfield = kpxcFields.isSearchField(input);

    document.body.removeChild(input);
    kpxcAssert(isSearchfield, expectedResult, Tests.SEARCH_FIELDS, testName);
}

async function assertSearchForm(properties, testName, expectedResult) {
    const form = kpxcUI.createElement('form', '', { action: 'search' });
    const input = kpxcUI.createElement('input', '', properties);
    form.appendChild(input);
    document.body.appendChild(form);

    const isSearchfield = kpxcFields.isSearchField(input);

    document.body.removeChild(form);
    kpxcAssert(isSearchfield, expectedResult, Tests.SEARCH_FIELDS, testName);
}

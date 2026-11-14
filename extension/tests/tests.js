'use strict';

const Tests = {
    GENERAL: '#general-results',
    INPUT_FIELDS: '#input-field-results',
    SEARCH_FIELDS: '#search-field-results',
};

function createResult(card, res, text) {
    const icon = kpxcUI.createElement('i', res ? 'fa fa-check' : 'fa fa-close', { id: text });
    const span = kpxcUI.createElement('span', '', '', text);
    const br = document.createElement('br');

    document.querySelector(card).appendMultiple(icon, span, br);
}

// Input field matching (keypaste.js)
async function testInputFields() {
    // Div ID, expected fields, action element ID (a button to be clicked)
    const testDivs = [
        [ 'basic1', 2 ], // Username/passwd fields
        [ 'basic2', 1 ], // Only username field
        [ 'basic3', 1 ], // Only password field
        [ 'basic4', 3 ], // Username/passwd/one more text field
        [ 'div1', 2, '#toggle1' ], // Fields are behind a button that must be pressed
        [ 'div2', 2, '#toggle2' ], // Fields are behind a button that must be pressed behind a JavaScript
        //[ 'div3', 2, '#toggle3' ], // Fields are behind a button that must be pressed
        //[ 'div4', 2, '#toggle4' ], // Fields are behind a button that must be pressed
        [ 'hiddenFields1', 0 ], // Two hidden fields
        //[ 'hiddenFields2', 1 ], // Two hidden fields with one visible
    ];

    for (const div of testDivs) {
        await assertInputFields(div[0], div[1], div[2]);
    }
}

// Search fields (kpxcFields
async function testSearchFields() {
    const searchFields = [
        [ '', { id: 'otp_field', name: 'otp', type: 'text', maxLength: '8' }, 'Generic 2FA field', false ],
        [ '', { placeholder: 'search', type: 'text', id: 'username' }, 'Placeholder only', true ],
        [ '', { ariaLabel: 'search', type: 'text', id: 'username' }, 'aria-label only', true ],

    ];

    for (const field of searchFields) {
        assertSearchField(field[0], field[1], field[2], field[3]);
    }

    assertSearchForm({ id: 'username', type: 'text', }, 'Generic input field under search form', true);
}

// Run tests
(async () => {
    await Promise.all([
        await testInputFields(),
        await testSearchFields(),
    ]);
})();

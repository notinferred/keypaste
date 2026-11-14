'use strict';

const formatTime = (time) => new Date(time).toLocaleString();

const showPairing = async function() {
    const pairing = await browser.runtime.sendMessage({ action: 'get_pairing' });
    const paired = document.getElementById('paired');
    const times = document.getElementById('paired-times');
    const notPaired = document.getElementById('not-paired');

    if (!pairing) {
        paired.hide();
        times.hide();
        notPaired.show();
        return;
    }

    document.getElementById('pairing-name').textContent = pairing.id;
    times.textContent = tr('optionsPairedTimes', [ formatTime(pairing.created), formatTime(pairing.lastUsed) ]);
    notPaired.hide();
    paired.show();
    times.show();
};

(async () => {
    document.getElementById('connect-button').addEventListener('click', async () => {
        const result = await browser.runtime.sendMessage({ action: 'associate' });
        document.getElementById('connect-result').hidden = result !== AssociatedAction.NOT_ASSOCIATED;
        await showPairing();
    });

    await showPairing();
})();

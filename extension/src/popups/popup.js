'use strict';

HTMLElement.prototype.show = function() {
    this.style.display = 'block';
};

HTMLElement.prototype.hide = function() {
    this.style.display = 'none';
};

function statusResponse(r) {
    $('#initial-state').hide();
    $('#error-encountered').hide();
    $('#need-reconfigure').hide();
    $('#not-configured').hide();
    $('#configured-and-associated').hide();
    $('#configured-not-associated').hide();
    $('#lock-database-button').hidden = true;
    $('#database-not-opened').hide();

    if (!r.keypasteAvailable) {
        $('#error-message').textContent = r.error;
        $('#error-encountered').show();
    } else if (r.keypasteAvailable && r.databaseClosed) {
        $('#database-error-message').textContent = r.error;
        $('#database-not-opened').show();
    } else if (!r.configured) {
        $('#not-configured').show();
    } else if (r.encryptionKeyUnrecognized) {
        $('#need-reconfigure').show();
        $('#need-reconfigure-message').textContent = r.error;
    } else if (!r.associated) {
        $('#need-reconfigure').show();
        $('#need-reconfigure-message').textContent = r.error;
    } else if (r.error) {
        $('#error-encountered').show();
        $('#error-message').textContent = r.error;
    } else {
        $('#configured-and-associated').show();
        $('#associated-identifier').textContent = r.identifier;
        $('#lock-database-button').hidden = false;

        if (r.iframeDetected) {
            $('#iframe-detected').hidden = false;
        }
    }
}

const sendMessageToTab = async function(message) {
    const tab = await getCurrentTab();
    if (!tab) {
        return false; // Only the background devtools or a popup are opened
    }

    await browser.tabs.sendMessage(tab.id, {
        action: message
    });

    return true;
};

(async () => {
    $('#connect-button').addEventListener('click', async () => {
        await browser.runtime.sendMessage({
            action: 'associate'
        });

        // This does not work with Firefox because of https://bugzilla.mozilla.org/show_bug.cgi?id=1665380
        await sendMessageToTab('retrieve_credentials_forced');
        close();
    });

    $('#reconnect-button').addEventListener('click', async () => {
        await browser.runtime.sendMessage({
            action: 'associate'
        });
        close();
    });

    $('#reload-status-button').addEventListener('click', async () => {
        statusResponse(await browser.runtime.sendMessage({
            action: 'reconnect'
        }));
    });

    $('#reopen-database-button').addEventListener('click', async () => {
        statusResponse(await browser.runtime.sendMessage({
            action: 'get_status',
            args: [ false, true ] // Set forcePopup to true
        }));
        window.close();
    });

    $('#redetect-fields-button').addEventListener('click', async () => {
        const res = await sendMessageToTab('redetect_fields');
        if (!res) {
            return;
        }

        statusResponse(await browser.runtime.sendMessage({
            action: 'get_status'
        }));
    });

    $('#lock-database-button').addEventListener('click', async () => {
        statusResponse(await browser.runtime.sendMessage({
            action: 'lock_database'
        }));
    });

    statusResponse(await browser.runtime.sendMessage({
        action: 'get_status'
    }).catch((err) => {
        logError('Could not get status: ' + err);
    }));
})();

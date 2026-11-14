'use strict';

(async () => {
    $('#lock-database-button').hidden = false;

    const tab = await getCurrentTab();
    if (!tab) {
        return [];
    }

    const logins = await getLoginData();
    const list = document.getElementById('login-list');

    for (const [ i, login ] of logins.entries()) {
        const button = createLoginButton(login);
        button.addEventListener('click', (e) => {
            if (!e.isTrusted) {
                return;
            }

            browser.tabs.sendMessage(tab?.id, {
                action: 'fill_user_pass_with_specific_login',
                id: i,
                uuid: login.uuid
            });

            close();
        });

        const item = document.createElement('li');
        item.dataset.search = login.text.toLowerCase();
        item.append(button);
        list.append(item);
    }

    if (logins.length > 1) {
        $('#filter-block').show();
        const filter = document.getElementById('login-filter');
        filter.addEventListener('input', (e) => {
            if (!e.isTrusted) {
                return;
            }

            const query = filter.value.toLowerCase();
            for (const item of list.children) {
                item.hidden = !item.dataset.search.includes(query);
            }
        });

        filter.focus();
    }

    $('#lock-database-button').addEventListener('click', (e) => {
        browser.runtime.sendMessage({
            action: 'lock_database'
        });

        $('#credentialsList').hide();
        $('#database-not-opened').show();
        $('#lock-database-button').hidden = true;
        $('#database-error-message').textContent = tr('errorMessageDatabaseNotOpened');
    });

    $('#reopen-database-button').addEventListener('click', (e) => {
        browser.runtime.sendMessage({
            action: 'get_status',
            args: [ false, true ] // Set forcePopup to true
        });
    });
})();

function createLoginButton(login) {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'login';

    const title = document.createElement('span');
    title.className = 'login-title';
    title.textContent = login.title ?? login.text;
    button.append(title);

    if (login.username !== undefined) {
        const username = document.createElement('span');
        username.className = 'login-username';
        username.textContent = login.expired ? `${login.username} · ${tr('credentialExpired')}` : login.username;
        button.append(username);
    }

    return button;
}

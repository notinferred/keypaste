'use strict';

/**
 * The list a click on keypaste's key icon opens: the site, the names of its logins in every open vault and the vaults that are
 * locked, then New login and Open keypaste. It holds no password; a pick asks keypaste for that one login and fills it.
 */
class CredentialAutocomplete extends Autocomplete {
    constructor() {
        super();
        this.answer = { state: 'unlocked', entries: [], lockedVaults: [], site: '', warning: '' };
    }

    // Asks keypaste for the field's logins by name and shows the list beside it, whatever the answer, so its state is always said.
    async open(field) {
        const combination = await kpxcFields.getCombination(field);
        kpxc.submitUrl = kpxc.getFormActionUrl(combination);
        this.answer = await kpxc.listLogins();
        this.elements = this.answer.entries ?? [];
        kpxc.credentials = this.elements;
        await this.showList(field);
    }

    rows() {
        const answer = this.answer;
        const rows = [];

        if (answer.site) {
            rows.push({ kind: 'site', text: answer.site });
        }

        if (answer.state === 'locked') {
            rows.push({ kind: 'action', icon: 'lock', text: tr('loginListKeypasteLocked'), button: tr('loginListUnlock'), primary: true, act: 'unlock-keypaste' });
            return rows;
        }

        if (answer.state !== 'unlocked') {
            const notes = { unpaired: 'loginListNotPaired', frame: 'credentialsBlockedInIframe', refused: 'loginListRefused' };
            rows.push({ kind: 'note', text: tr(notes[answer.state] ?? 'errorNotConnected'), warning: true });
            return rows;
        }

        if (answer.warning) {
            rows.push({ kind: 'note', text: answer.warning, warning: true });
        }

        for (const entry of this.elements) {
            rows.push({ kind: 'login', uuid: entry.uuid, title: entry.name, username: entry.login, vault: entry.vault });
        }

        for (const vault of answer.lockedVaults ?? []) {
            rows.push({ kind: 'locked-vault', icon: 'lock', text: tr('loginListVaultLocked', [ vault ]), button: tr('loginListUnlock'), act: 'unlock', vault });
        }

        if (this.elements.length === 0) {
            rows.push({ kind: 'note', text: answer.site ? tr('loginListNoLogins', [ answer.site ]) : tr('credentialsNoLoginsFound') });
        }

        rows.push({ kind: 'action', icon: 'plus', text: tr('loginListNewLogin'), act: 'new-login', vault: this.elements[0]?.vault ?? '' });
        if (this.elements.length > 0 || (answer.lockedVaults ?? []).length > 0) {
            rows.push({ kind: 'action', icon: 'external-link', text: tr('loginListOpenKeypaste'), act: 'open' });
        }

        return rows;
    }

    async itemClick(e, row) {
        if (!this.isGenuine(e)) {
            return;
        }

        e.stopPropagation();
        await this.pick(row.uuid);
    }

    async itemEnter(e, uuid) {
        if (!this.isGenuine(e)) {
            return;
        }

        await this.pick(uuid);
    }

    async actionClick(e, row) {
        if (!this.isGenuine(e)) {
            return;
        }

        e.stopPropagation();
        this.closeList();

        if (row.act === 'unlock-keypaste') {
            await sendMessage('get_database_hash', [ false, true ]); // Asks keypaste to show its unlock
        } else {
            await sendMessage('open_keypaste', [ row.act, row.vault ?? '' ]);
        }
    }

    // Fills the one login picked into the field's form; keypaste fills at once on a site it filled before, and otherwise asks.
    async pick(uuid) {
        const field = this.input;
        this.closeList();

        const combination = await kpxcFields.getCombination(field);
        await kpxcFill.fillPicked(combination, uuid);
        field?.focus();
    }
}

const kpxcUserAutocomplete = new CredentialAutocomplete();

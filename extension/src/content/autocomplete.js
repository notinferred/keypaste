'use strict';

const MAX_AUTOCOMPLETE_NAME_LEN = 50;

// A pick counts only once the list has been on screen this long, so a list slipped under a click already on its way fills nothing.
const LIST_SETTLE_MS = 500;

function cancelEvent(e) {
    e.preventDefault();
    e.stopPropagation();
    e.stopImmediatePropagation();
}

// Whether the page has made an element or one of its ancestors see-through, hidden or filtered, as an overlay attack does.
const isTampered = function(element) {
    for (let node = element; node && node.nodeType === Node.ELEMENT_NODE; node = node.parentElement) {
        const style = getComputedStyle(node);
        if (Number(style.opacity) < 1
            || style.visibility !== 'visible' && node === element
            || style.filter !== 'none'
            || style.mixBlendMode !== 'normal'
            || style.clipPath !== 'none'
            || style.mask && style.mask !== 'none') {
            return true;
        }
    }

    return false;
};

/**
 * keypaste's list of a field's logins: drawn in a closed shadow root in the browser's top layer, answering only trusted
 * events, and refusing a pick while the page has covered, faded or just slipped it under the pointer.
 */
class Autocomplete {
    constructor() {
        this.elements = [];
        this.index = -1;
        this.input = undefined;
        this.wrapper = undefined;
        this.shadowRoot = undefined;
        this.container = undefined;
        this.shownAt = 0;
    }

    clear() {
        this.elements = [];
    }

    async itemClick(e, item) {}

    async itemEnter(item) {}

    // The rows to draw: logins, then notes and actions, each { kind, ... }.
    rows() {
        return [];
    }

    ensureContainer() {
        if (this.wrapper) {
            return;
        }

        const styleSheet = createStylesheet('css/autocomplete.css');
        const colorStyleSheet = createStylesheet('css/colors.css');
        this.wrapper = kpxcUI.createElement('div');
        this.wrapper.style.all = 'unset';
        this.wrapper.style.display = 'none';
        styleSheet.addEventListener('load', () => (this.wrapper.style.display = 'block'));
        this.container = kpxcUI.createElement('div', 'kpxcAutocomplete-container', {
            id: 'kpxcAutocomplete-container',
            popover: 'manual',
        });
        this.container.style.margin = 0;

        this.shadowRoot = this.wrapper.attachShadow({ mode: 'closed' });
        this.shadowRoot.append(colorStyleSheet);
        this.shadowRoot.append(styleSheet);

        this.header = kpxcUI.createElement('div', 'kpxcAutocomplete-site');
        this.list = kpxcUI.createElement('div', 'kpxcAutocomplete-items', {
            id: 'kpxcAutocomplete-list',
            role: 'listbox',
            'aria-label': tr('autocompleteListLabel')
        });
        this.footer = kpxcUI.createElement('div', 'kpxcAutocomplete-footer');
        // Focus stays in the page's field, so the chosen login is announced through this line
        this.announcer = kpxcUI.createElement('div', 'kpxcAutocomplete-announcer', { role: 'status' });

        this.container.append(this.header, this.list, this.footer, this.announcer);
        this.shadowRoot.append(this.container);
        document.body.append(this.wrapper);
        kpxcUI.observeWrapper(this.wrapper);
    }

    async showList(inputField) {
        this.closeList();
        this.ensureContainer();
        this.input = inputField;

        if (!this.input.dataset.kpxcListKeys) {
            this.input.addEventListener('keydown', e => this.keyDown(e));
            this.input.dataset.kpxcListKeys = 'true';
        }

        this.draw();
        this.container.classList.add('kpxcAutocomplete-container--visible');
        if (kpxcFields.popoverSupported) {
            this.container.showPopover();
        }
        this.updatePosition();
        this.shownAt = performance.now();
        inputField.focus({ preventScroll: true });
    }

    draw() {
        for (const part of [ this.header, this.list, this.footer ]) {
            part.replaceChildren();
        }

        this.index = -1;
        for (const row of this.rows()) {
            const place = row.kind === 'site' || row.warning ? this.header : row.kind === 'action' ? this.footer : this.list;
            place.append(this.drawRow(row));
        }

        if (this.getAllItems().length > 0) {
            this.index = 0;
            this.selectItem();
        }
    }

    drawRow(row) {
        if (row.kind === 'site') {
            const site = kpxcUI.createElement('div', 'kpxcAutocomplete-site__name', {}, row.text);
            site.prepend(kpxcUI.createKeyMark());
            return site;
        }

        if (row.kind === 'note') {
            return kpxcUI.createElement('div', `kpxcAutocomplete-note${row.warning ? ' kpxcAutocomplete-note--warning' : ''}`,
                { role: row.warning ? 'alert' : 'note' }, row.text);
        }

        if (row.kind === 'login') {
            return this.drawLogin(row);
        }

        const action = kpxcUI.createElement('div', `kpxcAutocomplete-action${row.kind === 'locked-vault' ? ' kpxcAutocomplete-action--locked' : ''}`);
        const label = kpxcUI.createElement('span', 'kpxcAutocomplete-action__label', {}, row.text);
        action.append(kpxcUI.createIcon(row.icon), label);

        if (row.button) {
            const button = kpxcUI.createElement('button', `kpxcAutocomplete-button${row.primary ? ' kpxcAutocomplete-button--primary' : ''}`,
                { type: 'button' }, row.button);
            button.addEventListener('click', e => this.actionClick(e, row));
            action.append(button);
        } else {
            action.setAttribute('role', 'button');
            action.setAttribute('tabindex', '-1');
            action.addEventListener('click', e => this.actionClick(e, row));
        }

        action.addEventListener('mousedown', e => e.stopPropagation());
        return action;
    }

    drawLogin(row) {
        const username = row.username || tr('credentialsNoUsername');
        const item = kpxcUI.createElement('div', 'kpxcAutocomplete-item', {
            role: 'option',
            'aria-selected': 'false',
            'aria-label': [ row.title, username, row.vault ].filter(Boolean).join(', '),
        });
        item.dataset.uuid = row.uuid;

        const monogram = kpxcUI.createElement('div', 'kpxcAutocomplete-item__monogram', { 'aria-hidden': 'true' },
            (row.title.trim()[0] ?? '?').toUpperCase());
        const names = kpxcUI.createElement('div', 'kpxcAutocomplete-item__names');
        const title = row.title.length < MAX_AUTOCOMPLETE_NAME_LEN ? row.title : row.title.substr(0, MAX_AUTOCOMPLETE_NAME_LEN) + '…';
        names.append(
            kpxcUI.createElement('div', 'kpxcAutocomplete-item__label', {}, title),
            kpxcUI.createElement('div', 'kpxcAutocomplete-item__value', {}, username));
        item.append(monogram, names);

        if (row.vault) {
            item.append(kpxcUI.createElement('div', 'kpxcAutocomplete-item__vault', {}, row.vault));
        }

        item.addEventListener('click', e => this.itemClick(e, row));
        item.addEventListener('mousemove', e => this.mouseMove(e));
        item.addEventListener('mousedown', e => e.stopPropagation());
        item.addEventListener('mouseup', e => e.stopPropagation());
        return item;
    }

    async actionClick(e, row) {}

    /**
     * Whether a person's act on the list may count: a trusted event, on a list that has been on screen a moment, that is opaque,
     * unfiltered and the topmost thing where the pointer is.
     */
    isGenuine(e) {
        if (!e.isTrusted || !this.input || performance.now() - this.shownAt < LIST_SETTLE_MS) {
            return false;
        }

        const rect = this.container.getBoundingClientRect();
        if (isTampered(this.wrapper) || isTampered(this.container) || rect.width < 40 || rect.height < 20) {
            return false;
        }

        if (e.type === 'click' && e.detail > 0) {
            // A closed shadow root shows its host to the page's hit test: anything else on top is the page's.
            return document.elementFromPoint(e.clientX, e.clientY) === this.wrapper;
        }

        return true;
    }

    mouseMove(e) {
        if (e.movementX === 0 && e.movementY === 0) {
            return;
        }

        const items = Array.from(this.getAllItems());
        this.index = items.indexOf(e.currentTarget);
        this.selectItem();
    }

    selectItem() {
        this.deselectItem();
        const item = this.getAllItems()[this.index];
        if (item !== undefined) {
            item.classList.add('kpxcAutocomplete-item--active');
            item.setAttribute('aria-selected', 'true');
            item.scrollIntoView({ block: 'nearest' });
            this.announcer.textContent = item.getAttribute('aria-label');
        }
    }

    deselectItem() {
        this.list?.querySelectorAll('.kpxcAutocomplete-item--active').forEach(item => {
            item.classList.remove('kpxcAutocomplete-item--active');
            item.setAttribute('aria-selected', 'false');
        });
    }

    closeList() {
        this.input = undefined;
        if (!this.shadowRoot) {
            return;
        }

        this.container.classList.remove('kpxcAutocomplete-container--visible');
        this.announcer.textContent = '';
        if (kpxcFields.popoverSupported && this.container.matches(':popover-open')) {
            this.container.hidePopover();
        }
    }

    isOpen() {
        return this.input !== undefined;
    }

    getAllItems() {
        return this.list?.getElementsByClassName('kpxcAutocomplete-item') ?? [];
    }

    /**
     * Keys while the list is open, focus staying in the page's field: ArrowDown and ArrowUp choose a login, Enter fills the one
     * chosen, Escape closes. Nothing opens the list but the key icon.
     */
    async keyDown(e) {
        if (!e.isTrusted || !this.isOpen()) {
            return;
        }

        const items = this.getAllItems();
        if (e.key === 'ArrowDown' && items.length > 0) {
            cancelEvent(e);
            this.index = (this.index + 1) % items.length;
            this.selectItem();
        } else if (e.key === 'ArrowUp' && items.length > 0) {
            cancelEvent(e);
            this.index = (this.index > 0 ? this.index : items.length) - 1;
            this.selectItem();
        } else if (e.key === 'Enter' && items[this.index] !== undefined) {
            cancelEvent(e);
            await this.itemEnter(e, items[this.index].dataset.uuid);
        } else if (e.key === 'Escape' || e.key === 'Tab') {
            this.closeList();
        }
    }

    updatePosition() {
        if (!this.container || !this.input) {
            return;
        }

        const rect = this.input.getBoundingClientRect();
        this.container.style.minWidth = Pixels(Math.max(this.input.offsetWidth, 260));
        this.list.style.maxWidth = `max(${Pixels(this.input.offsetWidth)}, 420px)`;

        const zoom = kpxcUI.bodyStyle?.zoom || 1;
        const menuRect = this.container.getBoundingClientRect();
        const below = (rect.bottom + menuRect.height) / zoom <= window.self.visualViewport.height;

        // In the top layer the list is placed against the viewport, beside the field.
        if (kpxcFields.popoverSupported) {
            this.container.style.top = Pixels((below ? rect.bottom + 4 : rect.top - menuRect.height - 4) / zoom);
            this.container.style.left = Pixels(rect.left / zoom);
            return;
        }

        const scrollTop = kpxcUI.getScrollTop() / zoom;
        const scrollLeft = kpxcUI.getScrollLeft() / zoom;
        const top = below ? rect.bottom + 4 : rect.top - menuRect.height - 4;
        this.container.style.top = Pixels(top / zoom + scrollTop);
        this.container.style.left = Pixels(rect.left / zoom + scrollLeft);
    }
}


// Global handlers
const handleOutsideClick = function(e, autocompleteMenu) {
    if (e.target !== autocompleteMenu.input
        && e.target !== autocompleteMenu.wrapper
        && !e.target.classList?.contains('kpxc-username-icon')) {
        autocompleteMenu.closeList();
    }
};

// Detect click outside autocomplete
document.addEventListener('click', function(e) {
    if (!e.isTrusted) {
        return;
    }

    handleOutsideClick(e, kpxcUserAutocomplete);
});

window.addEventListener('resize', () => kpxcUserAutocomplete.updatePosition());
window.addEventListener('scroll', () => kpxcUserAutocomplete.updatePosition(), true);

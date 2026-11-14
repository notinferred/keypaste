'use strict';

const MIN_INPUT_FIELD_WIDTH_PX = 8;
const MIN_INPUT_FIELD_OFFSET_WIDTH = 60;
const MIN_OPACITY = 0.7;
const MAX_OPACITY = 1;

const ALLOWED_OBSERVER_NODETYPES = [
    Node.ELEMENT_NODE,
    Node.DOCUMENT_NODE,
    Node.DOCUMENT_FRAGMENT_NODE
];

const OBSERVER_OPTIONS = { attributes: true, attributeFilter: [ 'style' ] };

const DatabaseState = {
    DISCONNECTED: 0,
    LOCKED: 1,
    UNLOCKED: 2
};

let notificationWrapper;
let notificationTimeout;

// jQuery style wrapper for querySelector()
const $ = function(elem) {
    return document.querySelector(elem);
};

const kpxcUI = {};
kpxcUI.mouseDown = false;

if (document.body) {
    const bodyRect = document.body.getBoundingClientRect();
    kpxcUI.bodyRect = {
        left: bodyRect.left + window.pageXOffset,
        top: bodyRect.top + window.pageYOffset
    };
    kpxcUI.bodyStyle = getComputedStyle(document.body);
}

// Wrapper for creating elements
kpxcUI.createElement = function(type, classes, attributes, textContent) {
    const element = document.createElement(type);

    if (classes) {
        const splitted = classes.split(' ');
        for (const c of splitted) {
            element.classList.add(c);
        }
    }

    if (attributes !== undefined) {
        Object.keys(attributes).forEach((key) => {
            element.setAttribute(key, attributes[key]);
        });
    }

    if (textContent !== undefined) {
        element.textContent = textContent;
    }

    return element;
};

kpxcUI.getScrollTop = function() {
    return document.defaultView?.scrollY ?? document.scrollingElement?.scrollTop ?? 0;
};

kpxcUI.getScrollLeft = function() {
    return document.defaultView?.scrollX ?? document.scrollingElement?.scrollLeft ?? 0;
};

kpxcUI.getRelativeLeftPosition = function(rect) {
    return kpxcUI.bodyStyle.position.toLowerCase() === 'relative' ? rect.left - kpxcUI.bodyRect.left : rect.left;
};

kpxcUI.getRelativeTopPosition = function(rect) {
    return kpxcUI.bodyStyle.position.toLowerCase() === 'relative' ? rect.top - kpxcUI.bodyRect.top : rect.top;
};

kpxcUI.isRTL = function(field) {
    if (!field) {
        return false;
    }

    const style = getComputedStyle(field);
    if (style.textAlign.toLowerCase() === 'left') {
        return false;
    } else if (style.textAlign.toLowerCase() === 'right') {
        return true;
    }

    return kpxcFields.traverseParents(field,
        f => [ 'ltr', 'rtl' ].includes(f.getLowerCaseAttribute('dir')),
        f => ({ 'ltr': false, 'rtl': true })[f.getLowerCaseAttribute('dir')]);
};

// Lucide icons (ISC) the content scripts draw, as each SVG's child elements
const LUCIDE_ICONS = {
    'circle-check': [ [ 'circle', { cx: 12, cy: 12, r: 10 } ], [ 'path', { d: 'm9 12 2 2 4-4' } ] ],
    'circle-alert': [
        [ 'circle', { cx: 12, cy: 12, r: 10 } ],
        [ 'line', { x1: 12, x2: 12, y1: 8, y2: 12 } ],
        [ 'line', { x1: 12, x2: 12.01, y1: 16, y2: 16 } ]
    ],
    'triangle-alert': [
        [ 'path', { d: 'm21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3' } ],
        [ 'path', { d: 'M12 9v4' } ],
        [ 'path', { d: 'M12 17h.01' } ]
    ],
    'info': [ [ 'circle', { cx: 12, cy: 12, r: 10 } ], [ 'path', { d: 'M12 16v-4' } ], [ 'path', { d: 'M12 8h.01' } ] ],
    'x': [ [ 'path', { d: 'M18 6 6 18' } ], [ 'path', { d: 'm6 6 12 12' } ] ],
};

const NOTIFICATION_ICONS = {
    success: 'circle-check',
    info: 'info',
    warning: 'triangle-alert',
    error: 'circle-alert',
};

kpxcUI.createIcon = function(name) {
    const namespace = 'http://www.w3.org/2000/svg';
    const svg = document.createElementNS(namespace, 'svg');
    svg.setAttribute('class', 'kpxc-icon');
    svg.setAttribute('viewBox', '0 0 24 24');
    svg.setAttribute('aria-hidden', 'true');
    for (const [ tag, attributes ] of LUCIDE_ICONS[name]) {
        const child = document.createElementNS(namespace, tag);
        for (const [ key, value ] of Object.entries(attributes)) {
            child.setAttribute(key, value);
        }
        svg.append(child);
    }
    return svg;
};

/**
 * Creates a self-disappearing notification to DOM
 * @param {string} type     Notification type: (success, info, warning, error)
 * @param {string} message  The message shown
 */
kpxcUI.createNotification = async function(type, message) {
    if (!kpxc.settings.showNotifications || !type || !message) {
        return;
    }

    // Send the notification to top window from iframe
    if (window.self !== window.top) {
        await sendMessage('frame_message', [ 'notification_from_frame', type, message ]);
        return;
    }

    // Removes notification from the body element
    const removeNotification = function() {
        // Catch cross-domain exception
        let parentBody;
        try {
            parentBody = window.parent.document.body;
        } catch(_e) {
            parentBody = window.document.body;
        }

        if (notificationWrapper && parentBody.contains(notificationWrapper)) {
            parentBody.removeChild(notificationWrapper);
            notificationWrapper = undefined;
            return;
        }

        // Notification is not in the parent
        if (notificationWrapper && parentBody !== window.document.body && window.document.body.contains(notificationWrapper)) {
            window.document.body.removeChild(notificationWrapper);
            notificationWrapper = undefined;
        }
    };

    logDebug(message);

    const urgent = type === 'error' || type === 'warning';
    const notification = kpxcUI.createElement('div', 'kpxc-notification kpxc-notification-' + type,
        { role: urgent ? 'alert' : 'status' });
    const icon = kpxcUI.createIcon(NOTIFICATION_ICONS[type] ?? NOTIFICATION_ICONS.info);
    const text = kpxcUI.createElement('div', 'kpxc-notification-text');
    text.append(
        kpxcUI.createElement('span', 'kpxc-notification-source', {}, tr('popupTitle')),
        kpxcUI.createElement('span', 'kpxc-notification-message', {}, message)
    );
    const dismiss = kpxcUI.createElement('button', 'kpxc-notification-dismiss',
        { type: 'button', title: tr('notificationDismiss'), 'aria-label': tr('notificationDismiss') });
    dismiss.append(kpxcUI.createIcon('x'));

    notification.addEventListener('click', function() {
        removeNotification();
    });

    notification.appendMultiple(icon, text, dismiss);

    const colorStyleSheet = createStylesheet('css/colors.css');
    const styleSheet = createStylesheet('css/notification.css');
    notificationWrapper = notificationWrapper || document.createElement('div');
    notificationWrapper.style.all = 'unset';
    notificationWrapper.style.display = 'none';
    styleSheet.addEventListener('load', () => notificationWrapper.style.display = 'block');
    this.shadowRoot = notificationWrapper.attachShadow({ mode: 'closed' });
    if (!this.shadowRoot) {
        return;
    }

    this.shadowRoot.append(colorStyleSheet, styleSheet, notification);
    document.body.append(notificationWrapper);

    if (notificationTimeout) {
        clearTimeout(notificationTimeout);
    }

    // Destroy the notification after five seconds
    notificationTimeout = setTimeout(() => {
        removeNotification();
    }, 5000);
};

// Observe and prevent style changes to wrapper div elements
kpxcUI.createWrapperObserver = function() {
    kpxcUI.wrapperObserver = new MutationObserver(function(mutations, obs) {
        for (const mut of mutations) {
            if (mut?.target && mut.target.style?.cssText !== 'all: unset;') {
                mut.target.removeAttribute('style');
                mut.target.style.all = 'unset';
            }
        }
    });
};

kpxcUI.observeWrapper = function(elem) {
    kpxcUI.wrapperObserver?.observe(elem, OBSERVER_OPTIONS);
};

// Observer <html> and <body> style changes
kpxcUI.createPageObserver = function() {
    kpxcUI.pageObserver = new MutationObserver(function(mutations, obs) {
        for (const mut of mutations) {
            const currentStyle = getComputedStyle(mut?.target);
            if (currentStyle.opacity && currentStyle.opacity < MIN_OPACITY) {
                kpxc.clearAllFromPage();
            }
        }
    });

    if (document?.documentElement && ALLOWED_OBSERVER_NODETYPES.includes(document.documentElement.nodeType)) {
        kpxcUI.pageObserver.observe(document.documentElement, OBSERVER_OPTIONS);
    }
    if (document?.body && ALLOWED_OBSERVER_NODETYPES.includes(document.body.nodeType)) {
        kpxcUI.pageObserver.observe(document.body, OBSERVER_OPTIONS);
    }
};

const createStylesheet = function(file) {
    const stylesheet = document.createElement('link');
    stylesheet.setAttribute('rel', 'stylesheet');
    stylesheet.setAttribute('href', browser.runtime.getURL(file));
    return stylesheet;
};

const logDebug = function(message, extra) {
    if (kpxc.settings.debugLogging) {
        debugLogMessage(message, extra);
    }
};

const initObservers = function() {
    kpxcUI.createWrapperObserver();
    kpxcUI.createPageObserver();
};

document.addEventListener('mousedown', function(e) {
    if (!e.isTrusted) {
        return;
    }

    kpxcUI.mouseDown = true;
});

document.addEventListener('mouseup', function(e) {
    if (!e.isTrusted) {
        return;
    }

    kpxcUI.mouseDown = false;
});

if (document.readyState === 'complete' || (document.readyState !== 'loading' && !document.documentElement.doScroll)) {
    initObservers();
} else {
    document.addEventListener('DOMContentLoaded', initObservers);
}

HTMLDivElement.prototype.appendMultiple = function(...args) {
    for (const a of args) {
        this.append(a);
    }
};

Element.prototype.getLowerCaseAttribute = function(attr) {
    return this.getAttribute(attr) ? this.getAttribute(attr).toLowerCase() : undefined;
};

Element.prototype._attachShadow = Element.prototype.attachShadow;
Element.prototype.attachShadow = function () {
    try {
        return this._attachShadow({ mode: 'closed' });
    } catch (e) {
        logError(e);
    }
};

Object.prototype.shadowSelector = function(value) {
    return this.shadowRoot ? this.shadowRoot.querySelector(value) : undefined;
};

Object.prototype.shadowSelectorAll = function(value) {
    return this.shadowRoot ? this.shadowRoot.querySelectorAll(value) : undefined;
};

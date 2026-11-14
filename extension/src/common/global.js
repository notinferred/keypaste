'use strict';

const EXTENSION_NAME = 'keypaste';

// Credential sorting options
const SORT_BY_TITLE = 'sortByTitle';
const SORT_BY_USERNAME = 'sortByUsername';
const SORT_BY_GROUP_AND_TITLE = 'sortByGroupAndTitle';
const SORT_BY_GROUP_AND_USERNAME = 'sortByGroupAndUsername';
const SORT_BY_MATCHING_CREDENTIALS_SETTING = 'sortByMatchingCredentials';
const SORT_BY_RELEVANT_ENTRY = 'sortByRelevantEntry';

const URL_WILDCARD = '1kpxcwc1';

const AssociatedAction = {
    NOT_ASSOCIATED: 0,
    ASSOCIATED: 1,
    NEW_ASSOCIATION: 2,
    CANCELED: 3
};

const ManualFill = {
    NONE: 0,
    BOTH: 2
};

const isFirefox = function() {
    return browser.runtime.getURL('')?.startsWith('moz-extension');
};

const isSafari = function() {
    return browser.runtime.getURL('')?.startsWith('safari-web-extension');
};

const getIconClass = function(className) {
    if (isFirefox()) {
        return className + '-moz';
    } else if (isSafari()) {
        return className + '-safari';
    }
    return className;
};

// Returns a string with 'px' for CSS styles
const Pixels = function(value) {
    return String(value) + 'px';
};

// Checks if element's nodeName matches
const matchesWithNodeName = function(elem, name) {
    // Don't allow undefined element or 'name'
    if (!elem || !name) {
        return false;
    }

    return elem?.nodeName?.toUpperCase() === name?.toUpperCase();
};

// Match hostname or path with wildcards
const matchWithRegex = function(firstUrlPart, secondUrlPart, hostnameUsed = false) {
    if (firstUrlPart === secondUrlPart) {
        return true;
    }

    // If there's no wildcard with hostname, just compare directly
    if (hostnameUsed && !firstUrlPart.includes(URL_WILDCARD) && firstUrlPart !== secondUrlPart) {
        return false;
    }

    // Escape illegal characters
    let re = firstUrlPart.replaceAll(/[!\^$\+\-\(\)@<>]/g, '\\$&');
    if (hostnameUsed) {
        // Replace all host parts with wildcards so e.g. https://*.example.com is accepted with https://example.com
        re = re.replaceAll(`${URL_WILDCARD}.`, '(.*?)');
    }

    // Replace any remaining wildcards for paths
    re = re.replaceAll(URL_WILDCARD, '(.*?)');

    return secondUrlPart.match(new RegExp(re));
};

// Matches a site pattern, such as a predefined site's, with the current URL
const siteMatch = function(site, url) {
    try {
        site = site.replaceAll('*', URL_WILDCARD);
        const siteUrl = new URL(site);
        const currentUrl = new URL(url);

        // Match scheme and port. If the pattern does not use a port, all ports are ignored.
        if (siteUrl.protocol !== currentUrl.protocol || (siteUrl.port && siteUrl.port !== currentUrl.port)) {
            return false;
        }

        // Match hostname and path
        if (!matchWithRegex(siteUrl.hostname, currentUrl.hostname, true)
            || !matchWithRegex(siteUrl.pathname, currentUrl.pathname)) {
            return false;
        }

        return true;
    } catch(e) {
        logError(e);
    }

    return false;
};

function tr(key, params) {
    return browser.i18n.getMessage(key, params);
}

const debugLogMessage = function(message, extra) {
    console.debug(`[Debug ${getFileAndLine()}] ${EXTENSION_NAME} - ${message}`);

    if (extra) {
        console.debug(extra);
    }
};

const logError = function(message) {
    console.log(`[Error ${getFileAndLine()}] ${EXTENSION_NAME} - ${message}`);
};

// Returns file name and line number from error stack
const getFileAndLine = function() {
    const err = new Error().stack.split('\n');
    const line = err[4] ?? err.at(-1);
    const result = line.substring(line.lastIndexOf('/') + 1, line.lastIndexOf(':'));

    return result;
};

const getCurrentTab = async function() {
    const tabs = await browser.tabs.query({ active: true, currentWindow: true });
    return tabs?.length > 0 ? tabs[0] : undefined;
};

// Check if element b is inside a
const isElementInside = (a, b) => (b.x >= a.x && b.right <= a.right) && (b.y >= a.y && b.bottom <= a.bottom);

// Check if two elements overlap
const elementsOverlap = function(rect1, rect2) {
    const overlaps = (a, b) => !(a.right < b.left || a.left > b.right || a.bottom < b.top || a.top > b.bottom);
    return isElementInside(rect1, rect2) || overlaps(rect1, rect2);
};

// Exports for tests
if (typeof module === 'object') {
    module.exports = {
        elementsOverlap,
        matchesWithNodeName,
        siteMatch,
    };
}

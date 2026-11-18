'use strict';

const kpxcUsernameIcons = {};
kpxcUsernameIcons.icons = [];
kpxcUsernameIcons.detectedFields = [];

kpxcUsernameIcons.newIcon = function(field, databaseState = DatabaseState.DISCONNECTED) {
    kpxcUsernameIcons.icons.push(new UsernameFieldIcon(field, databaseState));
};

kpxcUsernameIcons.switchIcon = function(state) {
    kpxcUsernameIcons.icons.forEach(u => u.switchIcon(state));
};

kpxcUsernameIcons.isValid = function(field) {
    if (!field
        || field.offsetWidth < MIN_INPUT_FIELD_OFFSET_WIDTH
        || field.readOnly
        || kpxcIcons.hasIcon(field)
        || !kpxcFields.isVisible(field)) {
        return false;
    }

    return true;
};


class UsernameFieldIcon extends Icon {
    constructor(field, databaseState = DatabaseState.DISCONNECTED) {
        super(field, databaseState);

        this.initField(field);
        kpxcIcons.monitorIconPosition(this);
    }

    switchIcon(state) {
        if (!this.icon) {
            return;
        } else {
            this.observer.disconnect();
        }

        this.icon.classList.remove('lock', 'lock-moz', 'unlock', 'unlock-moz', 'disconnected', 'disconnected-moz');
        this.icon.classList.add(getIconClassName(state));
        this.icon.title = getIconText(state);

        this.icon.style.filter = state === DatabaseState.UNLOCKED ? 'saturate(100%)' : 'saturate(0%)';
    }
}

UsernameFieldIcon.prototype.initField = function(field) {
    // Observer the visibility
    if (this.observer) {
        this.observer.observe(field);
    }

    this.createIcon(field);
    this.inputField = field;
};

UsernameFieldIcon.prototype.createIcon = function(field) {
    const className = getIconClassName(this.databaseState);
    const size = this.calculateIconSize(field);

    // Don't create the icon if the input field is too small
    if (field.offsetWidth < (size * 1.5) || field.offsetHeight < size) {
        this.observer.unobserve(field);
        return;
    }

    const icon = kpxcUI.createElement('div', 'kpxc kpxc-username-icon ' + className,
        {
            'title': getIconText(this.databaseState),
            'size': size,
            'popover': 'manual'
        });

    if (kpxcFields.popoverSupported) {
        icon.style.margin = 0;
    } else {
        icon.style.zIndex = '10000000';
    }
    icon.style.width = Pixels(size);
    icon.style.height = Pixels(size);

    icon.addEventListener('click', function(e) {
        if (!e.isTrusted) {
            return;
        }

        if (e.shiftKey) {
            if (kpxcFields.popoverSupported) {
                icon.hidePopover();
            } else {
                icon.style.display = 'none';
            }
            return;
        }

        e.stopPropagation();
        iconClicked(field, icon);
    });

    icon.addEventListener('mousedown', ev => ev.stopPropagation());
    icon.addEventListener('mouseup', ev => ev.stopPropagation());

    kpxcIcons.setIconPosition(icon, field, this.rtl);
    this.icon = icon;
    this.createWrapper('css/username.css');
    if (kpxcFields.popoverSupported) {
        icon.showPopover();
    }
};

const iconClicked = async function(field, icon) {
    if (!kpxcFields.isVisible(field)) {
        icon.parentNode.removeChild(icon);
        return;
    }

    // The person's click is the only thing that asks keypaste for this page's logins; the list says if keypaste is locked.
    await kpxc.reconnect();
    await kpxcUserAutocomplete.open(field);
};

const getIconClassName = function(state = DatabaseState.UNLOCKED) {
    if (state === DatabaseState.LOCKED) {
        return getIconClass('lock');
    } else if (state === DatabaseState.DISCONNECTED) {
        return getIconClass('disconnected');
    }

    return getIconClass('unlock');
};

const getIconText = function(state) {
    if (state === DatabaseState.LOCKED) {
        return tr('usernameLockedFieldText');
    } else if (state === DatabaseState.DISCONNECTED) {
        return tr('usernameDisconnectedFieldText');
    }

    return tr('usernameFieldText');
};

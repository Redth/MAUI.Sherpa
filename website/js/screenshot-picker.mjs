export const screenshotIcons = {
    dashboard: 'fa-house',
    'dotnet-sdk': 'fa-layer-group',
    doctor: 'fa-stethoscope',
    'android-sdk': 'fa-cubes',
    'android-devices': 'fa-mobile-screen-button',
    keystores: 'fa-key',
    xcode: 'fa-hammer',
    'apple-simulators': 'fa-tablet-screen-button',
    certificates: 'fa-certificate',
    profiles: 'fa-file-signature',
    'bundle-ids': 'fa-fingerprint',
    secrets: 'fa-vault',
    publishing: 'fa-rocket',
    profiling: 'fa-chart-line',
    'app-inspector': 'fa-magnifying-glass',
    'firebase-push': 'fa-fire',
    'apple-push': 'fa-bell'
};

export function initScreenshotPicker(root, shots, onSelect) {
    const trigger = root.querySelector('[role="combobox"]');
    const list = root.querySelector('[role="listbox"]');
    const selectedIcon = trigger.querySelector('[data-picker-icon]');
    const selectedTitle = trigger.querySelector('[data-picker-title]');
    const selectedDescription = trigger.querySelector('[data-picker-description]');
    let selected = 0;
    let active = 0;
    let search = '';
    let lastTyped = 0;
    const options = shots.map((shot, index) => {
        const option = document.createElement('div');
        option.id = `screenshot-option-${shot.id}`;
        option.className = 'screenshot-picker__option';
        option.setAttribute('role', 'option');
        const icon = document.createElement('i');
        icon.className = `fas ${screenshotIcons[shot.id]} screenshot-picker__icon`;
        icon.setAttribute('aria-hidden', 'true');
        const copy = document.createElement('span');
        copy.className = 'screenshot-picker__copy';
        const title = document.createElement('span');
        title.className = 'screenshot-picker__title';
        title.textContent = shot.title;
        const description = document.createElement('span');
        description.className = 'screenshot-picker__description';
        description.textContent = shot.description;
        copy.append(title, description);
        const check = document.createElement('i');
        check.className = 'fas fa-check screenshot-picker__check';
        check.setAttribute('aria-hidden', 'true');
        option.append(icon, copy, check);
        option.addEventListener('pointerdown', event => event.preventDefault());
        option.addEventListener('pointermove', () => activate(index, false));
        option.addEventListener('click', () => {
            onSelect(shot.id);
            close();
            trigger.focus({ preventScroll: true });
        });
        list.append(option);
        return option;
    });

    function activate(index, scroll = true) {
        active = index;
        options.forEach((option, i) => option.classList.toggle('is-active', i === index));
        trigger.setAttribute('aria-activedescendant', options[index].id);
        if (scroll) options[index].scrollIntoView({ block: 'nearest' });
    }

    function open(index = selected) {
        list.hidden = false;
        trigger.setAttribute('aria-expanded', 'true');
        activate(index);
    }

    function close() {
        list.hidden = true;
        trigger.setAttribute('aria-expanded', 'false');
        trigger.removeAttribute('aria-activedescendant');
        search = '';
    }

    trigger.addEventListener('click', () => list.hidden ? open() : close());
    trigger.addEventListener('keydown', event => {
        const expanded = !list.hidden;
        if (event.key === 'Tab') {
            close();
            return;
        }
        if (event.key === 'Escape') {
            if (expanded) {
                event.preventDefault();
                event.stopPropagation();
                close();
            }
            return;
        }
        if (['ArrowDown', 'ArrowUp', 'Home', 'End', 'Enter', ' '].includes(event.key)) {
            event.preventDefault();
            if (event.key === 'Enter' || event.key === ' ') {
                if (!expanded) open();
                else {
                    onSelect(shots[active].id);
                    close();
                }
            } else {
                const index = event.key === 'Home' ? 0
                    : event.key === 'End' ? shots.length - 1
                    : !expanded ? selected
                    : (active + (event.key === 'ArrowDown' ? 1 : -1) + shots.length) % shots.length;
                if (!expanded) open(index);
                else activate(index);
            }
            return;
        }
        if (event.key.length === 1 && !event.ctrlKey && !event.metaKey && !event.altKey) {
            event.preventDefault();
            const now = Date.now();
            search = now - lastTyped > 700 ? event.key : search + event.key;
            lastTyped = now;
            const index = shots.findIndex(shot => shot.title.toLocaleLowerCase().startsWith(search.toLocaleLowerCase()));
            if (index >= 0) {
                if (!expanded) open(index);
                else activate(index);
            }
        }
    });
    document.addEventListener('pointerdown', event => {
        if (!root.contains(event.target)) close();
    });
    root.addEventListener('focusout', event => {
        if (!root.contains(event.relatedTarget)) close();
    });

    return {
        select(id) {
            selected = shots.findIndex(shot => shot.id === id);
            const shot = shots[selected];
            selectedIcon.className = `fas ${screenshotIcons[id]} screenshot-picker__icon`;
            selectedTitle.textContent = shot.title;
            selectedDescription.textContent = shot.description;
            options.forEach((option, index) => option.setAttribute('aria-selected', String(index === selected)));
            close();
        }
    };
}

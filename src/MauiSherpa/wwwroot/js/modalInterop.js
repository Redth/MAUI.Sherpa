// Modal keyboard navigation interop
// Provides focus trapping, Escape key handling, and auto-focus for Blazor modals
window.modalInterop = {
    _instances: new Map(),

    initialize(modalId, dotNetRef) {
        const modal = document.getElementById(modalId);
        if (!modal) return;

        const state = {
            dotNetRef,
            keyHandler: (e) => this._handleKeyDown(modalId, e),
        };

        modal.addEventListener('keydown', state.keyHandler);
        this._instances.set(modalId, state);

        // Auto-focus first focusable element
        requestAnimationFrame(() => this._focusFirst(modal));
    },

    dispose(modalId) {
        const state = this._instances.get(modalId);
        if (!state) return;

        const modal = document.getElementById(modalId);
        if (modal) {
            modal.removeEventListener('keydown', state.keyHandler);
        }
        this._instances.delete(modalId);
    },

    focusFirst(modalId) {
        const modal = document.getElementById(modalId);
        if (modal) {
            requestAnimationFrame(() => this._focusFirst(modal));
        }
    },

    _handleKeyDown(modalId, e) {
        const state = this._instances.get(modalId);
        if (!state) return;
        const modal = document.getElementById(modalId);
        if (!modal) return;

        if (e.key === 'Tab') {
            this._trapFocus(modal, e);
        } else if (e.key === 'Escape') {
            e.preventDefault();
            e.stopPropagation();
            state.dotNetRef.invokeMethodAsync('OnEscapePressed');
        }
    },

    _trapFocus(modal, e) {
        const focusable = this._getFocusableElements(modal);
        if (focusable.length === 0) return;

        e.preventDefault();

        const active = document.activeElement;
        let idx = focusable.indexOf(active);

        if (e.shiftKey) {
            idx = (idx <= 0) ? focusable.length - 1 : idx - 1;
        } else {
            idx = (idx < 0 || idx >= focusable.length - 1) ? 0 : idx + 1;
        }

        focusable[idx].focus();
    },

    _getFocusableElements(container) {
        const selector = [
            'button:not([disabled]):not([tabindex="-1"])',
            'input:not([disabled]):not([tabindex="-1"])',
            'select:not([disabled]):not([tabindex="-1"])',
            'textarea:not([disabled]):not([tabindex="-1"])',
            'a[href]:not([tabindex="-1"])',
            '[tabindex]:not([tabindex="-1"]):not([disabled])'
        ].join(', ');
        return Array.from(container.querySelectorAll(selector))
            .filter(el => el.offsetParent !== null); // visible only
    },

    _focusFirst(modal) {
        // Prefer primary action button, then first focusable
        const primary = modal.querySelector('.btn-primary:not([disabled])');
        if (primary) {
            primary.focus();
            return;
        }
        const focusable = this._getFocusableElements(modal);
        if (focusable.length > 0) {
            focusable[0].focus();
        }
    }
};

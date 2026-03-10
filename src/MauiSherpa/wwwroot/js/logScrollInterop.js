// Auto-scroll interop for pipeline log containers.
// Scrolls to bottom unless the user has manually scrolled up.
// Re-engages auto-scroll when user scrolls back to the bottom.
window.logScrollInterop = {
    _tracked: {},

    /**
     * Start tracking a log container for auto-scroll.
     * Attaches a scroll listener to detect user interrupts.
     */
    track: function (elementId) {
        const el = document.getElementById(elementId);
        if (!el) return;

        // Default: auto-scroll enabled
        this._tracked[elementId] = { autoScroll: true };

        el.addEventListener('scroll', () => {
            const state = this._tracked[elementId];
            if (!state) return;
            // If user is within 20px of the bottom, re-engage auto-scroll
            const atBottom = (el.scrollHeight - el.scrollTop - el.clientHeight) < 20;
            state.autoScroll = atBottom;
        });
    },

    /**
     * Scroll the container to the bottom if auto-scroll is active.
     */
    scrollToBottom: function (elementId) {
        const el = document.getElementById(elementId);
        if (!el) return;

        const state = this._tracked[elementId];
        if (!state || state.autoScroll) {
            el.scrollTop = el.scrollHeight;
        }
    },

    /**
     * Stop tracking a container (cleanup).
     */
    untrack: function (elementId) {
        delete this._tracked[elementId];
    }
};

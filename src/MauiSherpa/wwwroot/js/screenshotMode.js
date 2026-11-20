window.sherpaScreenshot = {
    initialize(reference) {
        this.setAppearance = theme => reference.invokeMethodAsync('SetAppearance', theme);
        this.beginCapture = () => reference.invokeMethodAsync('BeginCapture');
        this.closeCapture = () => reference.invokeMethodAsync('CloseCapture');
        const disableExternalActions = () => {
            for (const button of document.querySelectorAll('[data-screenshot-external]')) {
                if (!button.disabled) button.disabled = true;
                button.title = 'Unavailable in Screenshot Mode: external actions are disabled.';
                button.setAttribute('aria-disabled', 'true');
            }
        };
        this.observer?.disconnect();
        this.observer = new MutationObserver(disableExternalActions);
        this.observer.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['disabled'] });
        if (this.blockExternalClick) document.removeEventListener('click', this.blockExternalClick, true);
        this.blockExternalClick = event => {
            if (event.target.closest('[data-screenshot-external]')) {
                event.preventDefault();
                event.stopImmediatePropagation();
            }
        };
        document.addEventListener('click', this.blockExternalClick, true);
        disableExternalActions();
    }
};

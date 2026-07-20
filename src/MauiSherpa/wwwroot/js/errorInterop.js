(function () {
    const maxErrorLength = 4000;
    let latestError = '';

    function formatError(value) {
        if (value instanceof Error) {
            return value.stack || `${value.name}: ${value.message}`;
        }

        if (typeof value === 'string') {
            return value;
        }

        try {
            return JSON.stringify(value);
        } catch {
            return String(value);
        }
    }

    function captureError(message) {
        if (!message) {
            return;
        }

        latestError = String(message).slice(0, maxErrorLength);
        refreshErrorUi();
    }

    const originalConsoleError = console.error.bind(console);
    console.error = function (...args) {
        captureError(args.map(formatError).join(' '));
        originalConsoleError(...args);
    };

    window.addEventListener('error', (event) => {
        captureError(event.error?.stack || event.message);
    });

    window.addEventListener('unhandledrejection', (event) => {
        captureError(formatError(event.reason));
    });

    function getErrorUi() {
        return document.getElementById('blazor-error-ui');
    }

    function getIssueBody() {
        const details = latestError || 'No browser error details were captured. Please attach the Maui Sherpa application logs.';
        return [
            '## Unhandled Blazor error',
            '',
            '```text',
            details,
            '```',
            '',
            `User agent: ${navigator.userAgent}`
        ].join('\n');
    }

    function refreshErrorUi() {
        const errorUi = getErrorUi();
        if (!errorUi) {
            return;
        }

        const details = errorUi.querySelector('.error-details');
        const report = errorUi.querySelector('[data-error-action="report"]');
        if (details) {
            details.textContent = latestError || 'No browser error details were captured. Check the Maui Sherpa application logs for more information.';
        }
        if (report) {
            const query = new URLSearchParams({
                title: 'Unhandled Blazor error',
                body: getIssueBody()
            });
            report.href = `https://github.com/Redth/MAUI.Sherpa/issues/new?${query}`;
        }
    }

    function initializeErrorUi() {
        const errorUi = getErrorUi();
        if (!errorUi) {
            return;
        }

        const details = errorUi.querySelector('.error-details');
        const detailsButton = errorUi.querySelector('[data-error-action="details"]');
        const dismissButton = errorUi.querySelector('[data-error-action="dismiss"]');

        detailsButton?.addEventListener('click', () => {
            const isExpanded = detailsButton.getAttribute('aria-expanded') === 'true';
            detailsButton.setAttribute('aria-expanded', String(!isExpanded));
            detailsButton.textContent = isExpanded ? 'Show details' : 'Hide details';
            if (details) {
                details.hidden = isExpanded;
            }
            refreshErrorUi();
        });

        dismissButton?.addEventListener('click', () => {
            errorUi.style.display = 'none';
        });

        new MutationObserver(refreshErrorUi).observe(errorUi, {
            attributes: true,
            attributeFilter: ['style', 'class']
        });
        refreshErrorUi();
    }

    initializeErrorUi();
})();
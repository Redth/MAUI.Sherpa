(function () {
    const systemTheme = window.matchMedia("(prefers-color-scheme: dark)");

    function applySystemTheme() {
        const themeClass = systemTheme.matches ? "theme-dark" : "theme-light";
        const themedElements = [
            document.documentElement,
            document.body,
            document.querySelector(".main-layout")
        ].filter(Boolean);

        for (const element of themedElements) {
            element.classList.remove("theme-light", "theme-dark");
            element.classList.add(themeClass);
        }
    }

    applySystemTheme();
    systemTheme.addEventListener("change", applySystemTheme);

    const observer = new MutationObserver(applySystemTheme);
    observer.observe(document.body, { childList: true, subtree: true });

    const params = new URLSearchParams(window.location.search);
    const token = params.get("token");
    if (!token) {
        return;
    }

    const heartbeatUrl = `/internal/heartbeat?token=${encodeURIComponent(token)}`;

    async function heartbeat() {
        try {
            await fetch(heartbeatUrl, {
                method: "POST",
                cache: "no-store",
                keepalive: true
            });
        } catch {
        }
    }

    heartbeat();
    window.setInterval(heartbeat, 5000);

    window.addEventListener("pagehide", function () {
        try {
            navigator.sendBeacon(heartbeatUrl);
        } catch {
        }
    });
})();

(function () {
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

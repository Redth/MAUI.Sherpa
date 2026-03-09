window.profilingSample = {
    async runNetworkBurst(options) {
        const mode = options?.mode ?? 'local';
        const requestCount = Math.max(1, Math.min(40, options?.requestCount ?? 12));
        const timestamp = Date.now();
        const requests = Array.from({ length: requestCount }, (_, index) => createRequest(mode, index, timestamp));
        const results = await Promise.all(requests.map(executeRequest));
        return results;
    }
};

function createRequest(mode, index, timestamp) {
    if (mode === 'remote-mixed') {
        const remotePattern = index % 4;
        if (remotePattern === 0) {
            return { url: `https://jsonplaceholder.typicode.com/todos/${(index % 10) + 1}?cb=${timestamp}_${index}` };
        }

        if (remotePattern === 1) {
            return { url: `https://httpbin.org/delay/1?cb=${timestamp}_${index}` };
        }

        if (remotePattern === 2) {
            return { url: `https://jsonplaceholder.typicode.com/invalid-route-${index}?cb=${timestamp}_${index}` };
        }

        return { url: `https://httpbin.org/status/503?cb=${timestamp}_${index}` };
    }

    const assetPath = index % 2 === 0 ? 'css/app.css' : 'js/profilingSample.js';
    return { url: `${assetPath}?cb=${timestamp}_${index}` };
}

async function executeRequest(request) {
    const started = performance.now();

    try {
        const response = await fetch(request.url, {
            cache: 'no-store',
            headers: {
                'x-sherpa-profile': 'network-burst'
            }
        });

        const text = await response.text();
        return {
            url: request.url,
            statusCode: response.status,
            success: response.ok,
            durationMs: Math.round(performance.now() - started),
            error: response.ok ? null : `HTTP ${response.status}`,
            bytes: text.length
        };
    } catch (error) {
        return {
            url: request.url,
            statusCode: null,
            success: false,
            durationMs: Math.round(performance.now() - started),
            error: error instanceof Error ? error.message : String(error),
            bytes: 0
        };
    }
}

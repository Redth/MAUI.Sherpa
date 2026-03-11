// JS interop for loading speedscope with a profile file
// Speedscope is bundled at /speedscope/index.html and loaded in an iframe.
// When loaded with #localProfilePath=1, speedscope exposes window.speedscope.loadFileFromBase64().
// We poll for that API since speedscope sets it up asynchronously.

export function openSpeedscopeWithFile(iframeId, filePath, fileDataBase64, fileName) {
    return new Promise((resolve, reject) => {
        const iframe = document.getElementById(iframeId);
        if (!iframe) {
            reject('Speedscope iframe not found');
            return;
        }

        const tryInject = () => {
            try {
                const iframeWindow = iframe.contentWindow;
                if (!iframeWindow) {
                    reject('Cannot access iframe window');
                    return;
                }

                // Poll for speedscope's loadFileFromBase64 API.
                // When loaded with #localProfilePath=, speedscope sets up this API
                // asynchronously via a script tag. We poll until it's available.
                let attempts = 0;
                const maxAttempts = 20;
                const pollInterval = 250;

                const poll = () => {
                    attempts++;
                    if (iframeWindow.speedscope && iframeWindow.speedscope.loadFileFromBase64) {
                        iframeWindow.speedscope.loadFileFromBase64(fileName, fileDataBase64);
                        resolve(true);
                        return;
                    }

                    if (attempts < maxAttempts) {
                        setTimeout(poll, pollInterval);
                    } else {
                        // Final fallback: simulate a file drop
                        try {
                            const binaryString = atob(fileDataBase64);
                            const bytes = new Uint8Array(binaryString.length);
                            for (let i = 0; i < binaryString.length; i++) {
                                bytes[i] = binaryString.charCodeAt(i);
                            }
                            const file = new File([bytes], fileName, { type: 'application/octet-stream' });
                            const dataTransfer = new iframeWindow.DataTransfer();
                            dataTransfer.items.add(file);
                            const dropEvent = new iframeWindow.DragEvent('drop', {
                                bubbles: true,
                                cancelable: true,
                                dataTransfer: dataTransfer
                            });
                            iframeWindow.document.body.dispatchEvent(dropEvent);
                            resolve(true);
                        } catch (dropErr) {
                            reject('loadFileFromBase64 API not available and drop fallback failed: ' + dropErr.message);
                        }
                    }
                };

                poll();
            } catch (e) {
                reject('Failed to inject file into speedscope: ' + e.message);
            }
        };

        if (iframe.contentDocument && iframe.contentDocument.readyState === 'complete') {
            // Give speedscope a moment to initialize after DOM ready
            setTimeout(tryInject, 300);
        } else {
            iframe.addEventListener('load', () => setTimeout(tryInject, 300), { once: true });
        }
    });
}

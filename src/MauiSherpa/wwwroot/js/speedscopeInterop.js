// JS interop for loading speedscope with a profile file
// Speedscope is bundled at /speedscope/index.html and loaded in an iframe.
// We inject the file data into the iframe's speedscope instance by simulating
// a file drop using the DataTransfer API.

export function openSpeedscopeWithFile(iframeId, filePath, fileDataBase64, fileName) {
    return new Promise((resolve, reject) => {
        const iframe = document.getElementById(iframeId);
        if (!iframe) {
            reject('Speedscope iframe not found');
            return;
        }

        // Wait for iframe to load, then inject the file
        const tryInject = () => {
            try {
                const iframeWindow = iframe.contentWindow;
                if (!iframeWindow) {
                    reject('Cannot access iframe window');
                    return;
                }

                // Convert base64 to ArrayBuffer
                const binaryString = atob(fileDataBase64);
                const bytes = new Uint8Array(binaryString.length);
                for (let i = 0; i < binaryString.length; i++) {
                    bytes[i] = binaryString.charCodeAt(i);
                }

                // Create a File object
                const file = new File([bytes], fileName, { type: 'application/octet-stream' });

                // Use speedscope's loadFileFromBase64 API if available
                if (iframeWindow.speedscope && iframeWindow.speedscope.loadFileFromBase64) {
                    iframeWindow.speedscope.loadFileFromBase64(fileName, fileDataBase64);
                    resolve(true);
                    return;
                }

                // Fallback: simulate a file drop on the document body
                const dataTransfer = new iframeWindow.DataTransfer();
                dataTransfer.items.add(file);

                const dropEvent = new iframeWindow.DragEvent('drop', {
                    bubbles: true,
                    cancelable: true,
                    dataTransfer: dataTransfer
                });

                iframeWindow.document.body.dispatchEvent(dropEvent);
                resolve(true);
            } catch (e) {
                reject('Failed to inject file into speedscope: ' + e.message);
            }
        };

        if (iframe.contentDocument && iframe.contentDocument.readyState === 'complete') {
            // Give speedscope a moment to initialize after DOM ready
            setTimeout(tryInject, 500);
        } else {
            iframe.addEventListener('load', () => setTimeout(tryInject, 500), { once: true });
        }
    });
}

// Convert a .nettrace file to speedscope format by reading the pre-converted file
export function readFileAsBase64(filePath) {
    // This is handled on the C# side — we can't read local files from JS
    return null;
}

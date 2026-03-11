// JS interop for loading speedscope with a profile file.
// Sends the profile data to the speedscope iframe via postMessage.
// The iframe's index.html has a message listener that handles the load.
// Sends multiple times to handle timing — the listener deduplicates.

export function openSpeedscopeWithFile(iframeId, fileDataBase64, fileName) {
    return new Promise((resolve, reject) => {
        const iframe = document.getElementById(iframeId);
        if (!iframe) {
            reject('Speedscope iframe not found');
            return;
        }

        const msg = { type: 'loadProfile', name: fileName, base64: fileDataBase64 };

        const trySend = () => {
            try {
                if (iframe.contentWindow) {
                    iframe.contentWindow.postMessage(msg, '*');
                }
            } catch (e) {
                // contentWindow not accessible — will retry
            }
        };

        // Send multiple times at increasing intervals to handle all timing scenarios:
        // - iframe not yet loaded
        // - speedscope JS not yet initialized
        // - message listener not yet registered
        const delays = [500, 1000, 1500, 2000, 3000, 5000];
        delays.forEach(d => setTimeout(trySend, d));

        // Resolve after first send attempt
        setTimeout(() => resolve(true), 600);
    });
}

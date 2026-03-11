// JS interop for loading speedscope with a profile file.
// Sends the profile data to the speedscope iframe via postMessage.
// The iframe's index.html has a message listener that handles the load.

export function openSpeedscopeWithFile(iframeId, filePath, fileDataBase64, fileName) {
    return new Promise((resolve, reject) => {
        const iframe = document.getElementById(iframeId);
        if (!iframe) {
            reject('Speedscope iframe not found');
            return;
        }

        const sendData = () => {
            if (!iframe.contentWindow) {
                reject('Cannot access iframe window');
                return;
            }
            iframe.contentWindow.postMessage({
                type: 'loadProfile',
                name: fileName,
                base64: fileDataBase64
            }, '*');
            resolve(true);
        };

        // Ensure iframe is loaded before sending
        if (iframe.contentDocument && iframe.contentDocument.readyState === 'complete') {
            setTimeout(sendData, 500);
        } else {
            iframe.addEventListener('load', () => setTimeout(sendData, 500), { once: true });
        }
    });
}

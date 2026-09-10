// Hands a downloaded file to the browser. The inspector runs both in a WebView and in a tab, and an
// anchor with a Blob URL is the one save path that works in both.
export function saveFile(fileName, base64, contentType) {
    const binary = atob(base64);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) {
        bytes[i] = binary.charCodeAt(i);
    }

    const url = URL.createObjectURL(new Blob([bytes], { type: contentType || "application/octet-stream" }));
    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);

    // Revoking immediately can beat the download in some engines; a tick is enough.
    setTimeout(() => URL.revokeObjectURL(url), 10000);
}

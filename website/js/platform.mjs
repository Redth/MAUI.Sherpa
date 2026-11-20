export const platformStorageKey = 'sherpa-screenshot-platform';
export const platformIds = ['macos', 'windows', 'linux'];

export function detectPlatform(navigatorInfo = {}) {
    const { userAgent = '', platform = '', userAgentData, maxTouchPoints = 0 } = navigatorInfo;
    if (/Android|iPhone|iPad|iPod/i.test(userAgent) || (/Mac/i.test(platform) && maxTouchPoints > 1)) return null;
    const identity = `${userAgentData?.platform || ''} ${platform} ${userAgent}`;
    if (/Windows|Win32|Win64/i.test(identity)) return 'windows';
    if (/macOS|Macintosh|MacIntel/i.test(identity)) return 'macos';
    if (/Linux/i.test(identity) && !/CrOS/i.test(identity)) return 'linux';
    return null;
}

export function readPlatformPreference() {
    try {
        const platform = localStorage.getItem(platformStorageKey);
        return platformIds.includes(platform) ? platform : null;
    } catch (error) {
        console.warn('Platform preference storage is unavailable; using browser detection.', error);
        return null;
    }
}

export function savePlatformPreference(platform) {
    if (!platformIds.includes(platform)) return;
    try {
        localStorage.setItem(platformStorageKey, platform);
    } catch (error) {
        console.warn('The platform choice will apply only to this page because browser storage is unavailable.', error);
    }
}

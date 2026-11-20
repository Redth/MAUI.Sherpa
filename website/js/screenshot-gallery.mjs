import { detectPlatform, readPlatformPreference, savePlatformPreference } from './platform.mjs';
import { initScreenshotPicker } from './screenshot-picker.mjs';

export function resolveCapture(manifest, platform, shotId) {
    const shot = manifest.shots.find(shot => shot.id === shotId);
    if (!shot || !Object.hasOwn(manifest.platforms, platform)) throw new Error('Unknown screenshot or platform.');
    if (!shot.platforms.includes(platform)) return { state: 'unsupported', shot };
    const src = shot.assets[platform];
    return { state: src ? 'ready' : 'pending', shot, src };
}

export function initialPlatform(manifest, preference, detected) {
    if (Object.hasOwn(manifest.platforms, preference)) return preference;
    if (Object.hasOwn(manifest.platforms, detected) && manifest.shots.some(shot => shot.assets[detected])) return detected;
    return 'macos';
}

export async function initScreenshotGallery() {
    const stage = document.getElementById('screenshot-stage');
    if (!stage) return;
    const notice = document.getElementById('tour-notice');
    const response = await fetch('screenshots.json');
    if (!response.ok) throw new Error(`Screenshot inventory request failed (${response.status}).`);
    const manifest = await response.json();
    const detected = detectPlatform(navigator);
    const preference = readPlatformPreference();
    let platform = initialPlatform(manifest, preference, detected);
    let shotId = 'dashboard';
    let loadGeneration = 0;
    const image = document.getElementById('tour-image');
    const openButton = document.getElementById('screenshot-open');
    const empty = document.getElementById('tour-empty');
    const picker = initScreenshotPicker(document.getElementById('screenshot-picker'), manifest.shots, selectShot);
    const shortcuts = document.getElementById('tour-shortcuts');
    const platformButtons = [...document.querySelectorAll('[data-platform]')];
    const emptyTitle = document.getElementById('tour-empty-title');
    const emptyDescription = document.getElementById('tour-empty-description');
    const macosButton = document.getElementById('show-macos');

    function updateDownloads(target) {
        document.querySelectorAll('[data-platform-download]').forEach(link => {
            link.href = `getting-started.html#${target}`;
        });
    }
    updateDownloads(preference || detected || 'macos');

    for (const shot of manifest.shots) {
        if (shot.featured) {
            const button = document.createElement('button');
            button.type = 'button';
            button.dataset.shot = shot.id;
            button.textContent = shot.title;
            button.setAttribute('aria-controls', 'screenshot-stage');
            button.addEventListener('click', () => selectShot(shot.id));
            shortcuts.append(button);
        }
    }
    document.getElementById('tour-controls').hidden = false;
    shortcuts.hidden = false;
    if (!preference && detected && platform !== detected) {
        notice.textContent = `${manifest.platforms[detected].label} is available too. Its screenshot set is being prepared; you're viewing real macOS captures for now. Use the platform switcher to explore.`;
        notice.hidden = false;
    }

    function showEmpty(title, description, offerMacos = true) {
        stage.setAttribute('aria-busy', 'false');
        openButton.hidden = true;
        empty.hidden = false;
        emptyTitle.textContent = title;
        emptyDescription.textContent = description;
        macosButton.hidden = !offerMacos;
    }

    function render() {
        const generation = ++loadGeneration;
        const { state, shot, src } = resolveCapture(manifest, platform, shotId);
        const { label, native } = manifest.platforms[platform];
        platformButtons.forEach(button => button.setAttribute('aria-pressed', String(button.dataset.platform === platform)));
        shortcuts.querySelectorAll('button').forEach(button => button.setAttribute('aria-pressed', String(button.dataset.shot === shotId)));
        picker.select(shotId);
        document.getElementById('tour-title').textContent = shot.title;
        document.getElementById('tour-description').textContent = shot.description;
        document.getElementById('tour-platform-label').textContent = `${label} · ${native}`;
        const download = document.getElementById('tour-platform-download');
        download.href = `getting-started.html#${platform}`;
        download.textContent = `Get for ${label}`;
        if (state === 'unsupported') {
            showEmpty('This tool needs macOS', `${shot.title} uses local Apple development tools. Explore its macOS screenshot, or choose another feature to see what's available on ${label}.`);
            return;
        }
        if (state === 'pending') {
            const hasOtherCaptures = manifest.shots.some(item => item.assets[platform]);
            showEmpty(
                hasOtherCaptures ? 'This view is being prepared' : `${label} screenshots are on the way`,
                hasOtherCaptures
                    ? `A current ${label} capture of ${shot.title} isn't available yet. Choose another feature to keep exploring.`
                    : `Sherpa is available for ${label}. We're preparing matching captures of the native ${native} app. Meanwhile, you can explore the macOS collection.`,
                platform !== 'macos'
            );
            return;
        }
        empty.hidden = true;
        openButton.hidden = true;
        stage.setAttribute('aria-busy', 'true');
        const preload = new Image();
        preload.onload = () => {
            if (generation !== loadGeneration) return;
            image.width = preload.naturalWidth;
            image.height = preload.naturalHeight;
            stage.style.aspectRatio = `${preload.naturalWidth} / ${preload.naturalHeight}`;
            image.src = src;
            image.alt = `MAUI Sherpa on ${label}: ${shot.title}. ${shot.description}`;
            openButton.setAttribute('aria-label', `Enlarge ${label} ${shot.title} screenshot`);
            openButton.hidden = false;
            stage.setAttribute('aria-busy', 'false');
        };
        preload.onerror = () => {
            if (generation !== loadGeneration) return;
            console.error(`Screenshot failed to load: ${src}`);
            showEmpty("This screenshot couldn't load", 'Choose another feature or reload the page to try again.', false);
        };
        preload.src = src;
    }

    function selectShot(id) {
        shotId = id;
        render();
    }
    platformButtons.forEach(button => {
        button.addEventListener('click', () => {
            platform = button.dataset.platform;
            savePlatformPreference(platform);
            updateDownloads(platform);
            notice.hidden = true;
            render();
        });
    });
    macosButton.addEventListener('click', () => {
        platformButtons.find(button => button.dataset.platform === 'macos').click();
    });
    document.querySelectorAll('[data-show-screenshot]').forEach(link => {
        link.addEventListener('click', () => selectShot(link.dataset.showScreenshot));
    });
    render();
}

if (typeof document !== 'undefined') {
    initScreenshotGallery().catch(error => {
        console.error('The screenshot explorer could not be initialized.', error);
        const notice = document.getElementById('tour-notice');
        if (notice) {
            notice.textContent = "The screenshot explorer couldn't load. The macOS Dashboard is shown below; reload the page to try again.";
            notice.hidden = false;
        }
    });
}

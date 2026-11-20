import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { detectPlatform } from '../website/js/platform.mjs';
import { initialPlatform, resolveCapture } from '../website/js/screenshot-gallery.mjs';

const manifest = JSON.parse(readFileSync(new URL('../website/screenshots.json', import.meta.url), 'utf8'));

test('desktop platform detection handles client hints and desktop browsers without misidentifying mobile devices', () => {
    const cases = [
        [{ userAgentData: { platform: 'Windows' } }, 'windows'],
        [{ platform: 'Win32', userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)' }, 'windows'],
        [{ platform: 'MacIntel', userAgent: 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)' }, 'macos'],
        [{ platform: 'Linux x86_64' }, 'linux'],
        [{ platform: 'Linux armv8l', userAgent: 'Mozilla/5.0 (Linux; Android 16)' }, null],
        [{ platform: 'MacIntel', userAgent: 'Mozilla/5.0 (Macintosh)', maxTouchPoints: 5 }, null],
        [{ userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 19_0 like Mac OS X)' }, null],
        [{ userAgent: 'Mozilla/5.0 (X11; CrOS x86_64)', platform: 'Linux x86_64' }, null],
        [{}, null]
    ];
    for (const [info, expected] of cases) assert.equal(detectPlatform(info), expected, JSON.stringify(info));
});

test('automatic selection only defaults to platforms with actual captures', () => {
    const withoutWindows = structuredClone(manifest);
    for (const shot of withoutWindows.shots) delete shot.assets.windows;
    assert.equal(initialPlatform(withoutWindows, null, 'windows'), 'macos');
    const withWindows = structuredClone(withoutWindows);
    withWindows.shots[0].assets.windows = 'images/screenshots/windows/dashboard.webp';
    assert.equal(initialPlatform(withWindows, null, 'windows'), 'windows');
});

test('a saved explicit platform is respected even when its screenshots are pending', () => {
    assert.equal(initialPlatform(manifest, 'linux', 'macos'), 'linux');
    assert.equal(initialPlatform(manifest, 'invalid', null), 'macos');
    assert.equal(initialPlatform(manifest, 'toString', null), 'macos');
});

test('missing platform captures never silently return another platforms image', () => {
    const unavailable = structuredClone(manifest);
    delete unavailable.shots.find(shot => shot.id === 'dashboard').assets.windows;
    const capture = resolveCapture(unavailable, 'windows', 'dashboard');
    assert.equal(capture.state, 'pending');
    assert.equal(capture.src, undefined);
    assert.equal(resolveCapture(manifest, 'macos', 'dashboard').state, 'ready');
});

test('screenshot URLs change when a platform receives new captures', () => {
    const updated = structuredClone(manifest);
    updated.platforms.macos.capture.capturedAt = '2026-11-21T10:00:00.000Z';
    const original = resolveCapture(manifest, 'macos', 'dashboard');
    const next = resolveCapture(updated, 'macos', 'dashboard');
    assert.notEqual(original.src, next.src);
    assert.equal(new URL(next.src, 'https://example.com/').searchParams.get('v'), updated.platforms.macos.capture.capturedAt);
    assert.equal(resolveCapture(updated, 'windows', 'dashboard').src, resolveCapture(manifest, 'windows', 'dashboard').src);
});

test('macOS-only tools are distinguished from captures that have not been supplied yet', () => {
    assert.equal(resolveCapture(manifest, 'windows', 'xcode').state, 'unsupported');
    assert.equal(resolveCapture(manifest, 'linux', 'apple-simulators').state, 'unsupported');
    assert.throws(() => resolveCapture(manifest, 'macos', 'missing'), /Unknown screenshot/);
    assert.throws(() => resolveCapture(manifest, 'toString', 'dashboard'), /Unknown screenshot/);
});

test('every advertised capture is a real local asset for a supported platform', () => {
    const ids = new Set();
    for (const shot of manifest.shots) {
        assert.ok(!ids.has(shot.id), `Duplicate screenshot id: ${shot.id}`);
        ids.add(shot.id);
        for (const [platform, src] of Object.entries(shot.assets)) {
            assert.ok(shot.platforms.includes(platform), `${shot.id} is not supported on ${platform}`);
            assert.match(src, new RegExp(`^images/screenshots/${platform}/[a-z0-9-]+\\.webp$`));
            const asset = new URL(`../website/${src}`, import.meta.url);
            assert.ok(existsSync(asset), `Missing screenshot: ${fileURLToPath(asset)}`);
        }
    }
});

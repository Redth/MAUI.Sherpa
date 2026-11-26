#!/usr/bin/env node
import { spawn, spawnSync } from 'node:child_process';
import { createInterface } from 'node:readline';
import { createInterface as createPrompt } from 'node:readline/promises';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseArgs } from 'node:util';
import { setTimeout as delay } from 'node:timers/promises';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const { values } = parseArgs({
    options: {
        'agent-port': { type: 'string' },
        platform: { type: 'string' },
        output: { type: 'string' },
        theme: { type: 'string', default: 'dark' },
        shots: { type: 'string' },
        timeout: { type: 'string', default: '30' },
        width: { type: 'string', default: '1440' },
        height: { type: 'string', default: '960' },
        'pixel-ratio': { type: 'string', default: '2' },
        confirmed: { type: 'boolean', default: false },
        'check-only': { type: 'boolean', default: false },
        help: { type: 'boolean', default: false }
    }
});

const headings = {
    dashboard: 'Dashboard', 'dotnet-sdk': '.NET SDK Manager', doctor: 'Environment Doctor',
    'android-sdk': 'Android SDK & JDK', 'android-devices': 'Android Devices',
    keystores: 'Keystores', xcode: 'Xcode', 'apple-simulators': 'Apple Devices',
    certificates: 'Certificates', profiles: 'Provisioning Profiles', 'bundle-ids': 'Bundle',
    secrets: 'Secrets', publishing: 'Publish Profiles', profiling: 'Profiling',
    'app-inspector': 'MAUI DevFlow Inspector', 'firebase-push': 'Firebase Push Testing',
    'apple-push': 'Push Testing'
};

class DevFlow {
    constructor(port, timeout) {
        this.timeout = timeout;
        this.child = spawn('dotnet', ['maui', 'devflow', 'batch', '--agent-port', port], {
            cwd: join(root, 'src', 'MauiSherpa'), stdio: ['pipe', 'pipe', 'pipe']
        });
        this.stderr = '';
        this.child.stderr.on('data', chunk => { this.stderr += chunk; });
        createInterface({ input: this.child.stdout }).on('line', line => {
            try {
                const result = JSON.parse(line);
                const pending = this.pending;
                if (!pending) return;
                this.pending = null;
                clearTimeout(pending.timer);
                if (result.exit_code !== 0 || result.output.startsWith('Error:'))
                    pending.reject(new Error(`${result.command}: ${result.output}`));
                else
                    pending.resolve(result.output.replace(/\r?\n\u2192 target:[^\n]*$/, ''));
            } catch (error) {
                this.fail(error);
            }
        });
        this.child.on('error', error => this.fail(error));
        this.child.on('exit', code => this.fail(new Error(`DevFlow exited (${code}): ${this.stderr}`)));
    }

    fail(error) {
        if (this.pending) {
            clearTimeout(this.pending.timer);
            this.pending.reject(error);
            this.pending = null;
        }
    }

    command(command) {
        if (this.pending) throw new Error('DevFlow commands must be sequential.');
        return new Promise((resolve, reject) => {
            const timer = setTimeout(() => {
                this.pending = null;
                reject(new Error(`DevFlow command timed out: ${command}`));
            }, this.timeout);
            this.pending = { resolve, reject, timer };
            this.child.stdin.write(command + '\n');
        });
    }

    evaluate(expression) {
        if (/["\r\n]/.test(expression)) throw new Error('Batch expressions must use single quotes.');
        return this.command(`cdp Runtime evaluate "${expression}"`);
    }

    async close() {
        this.child.stdin.end();
        if (this.child.exitCode !== null) return;
        await new Promise(resolve => {
            const timeout = setTimeout(() => { this.child.kill(); resolve(); }, 5000);
            this.child.once('exit', () => { clearTimeout(timeout); resolve(); });
        });
    }
}

const stateExpression = `JSON.stringify((()=>{
    const visible=e=>e.getClientRects().length>0&&getComputedStyle(e).visibility!=='hidden';
    const diagnostics=document.querySelector('#screenshot-diagnostics');
    const text=selector=>Array.from(document.querySelectorAll(selector)).filter(visible).map(e=>e.innerText.trim());
    return {
        diagnostics:diagnostics?{...diagnostics.dataset}:null,
        route:location.pathname,title:document.querySelector('h1')?.innerText.trim(),
        tabs:text('.tabs .tab'),
        activeTab:text('.tabs .tab.active')[0]??null,
        loading:text('.loading-state,.loading-inline,.loading-card,.loading-indicator,.fa-spin,[aria-busy]:not([aria-busy=false])').length,
        errors:text('.error-bar,.error-message,.error-banner,.environment-error,.alert-danger,#blazor-error-ui'),
        blurred:Array.from(document.querySelectorAll('.demo-blur')).some(visible),
        debugMenu:!!document.querySelector('.sidebar a[href=debug]'),
        sidebarOverflow:Array.from(document.querySelectorAll('.sidebar-menu')).filter(visible).some(e=>e.scrollHeight>e.clientHeight+1),
        remoteResources:performance.getEntriesByType('resource').filter(e=>/^https?:/.test(e.name)&&new URL(e.name).origin!==location.origin).map(e=>e.name),
        fingerprint:document.querySelector('main')?.innerText??document.body.innerText
    };
})())`.replaceAll('\n', ' ');

async function main() {
    if (values.help) {
        console.log('node build/capture-screenshots.mjs --agent-port PORT --platform macos|windows|linux --output DIRECTORY [--theme dark|light|both] [--shots dashboard,doctor] [--check-only] [--confirmed] [--width 1440 --height 960 --pixel-ratio 2]');
        return;
    }
    if (!/^\d+$/.test(values['agent-port'] ?? '') || !['macos', 'windows', 'linux'].includes(values.platform)
        || !values.output || !['dark', 'light', 'both'].includes(values.theme))
        throw new Error('Provide --agent-port, --platform, --output and a valid --theme. Use --help for usage.');
    const timeout = Number(values.timeout) * 1000;
    const width = Number(values.width), height = Number(values.height);
    const pixelRatio = Number(values['pixel-ratio']);
    if (!Number.isFinite(timeout) || timeout <= 0 || !Number.isInteger(width) || !Number.isInteger(height) || width < 800 || height < 600)
        throw new Error('Use a positive timeout and integer dimensions of at least 800 x 600.');
    if (!Number.isFinite(pixelRatio) || pixelRatio < 1 || !Number.isInteger(width * pixelRatio) || !Number.isInteger(height * pixelRatio))
        throw new Error('Use a pixel ratio of at least 1 that produces integer pixel dimensions.');
    const manifest = JSON.parse(readFileSync(join(root, 'website', 'screenshots.json'), 'utf8'));
    const requested = values.shots?.split(',');
    const shots = manifest.shots.filter(shot => shot.platforms.includes(values.platform) && (!requested || requested.includes(shot.id)));
    if (!shots.length || requested?.some(id => !shots.some(shot => shot.id === id)))
        throw new Error('Unknown or unsupported screenshot ID for this platform.');
    if (shots.some(shot => !headings[shot.id] || !/^[a-z0-9/-]*$/.test(shot.route) || !/^[a-z0-9-]+$/.test(shot.id)))
        throw new Error('The manifest contains an unsupported capture route or missing readiness heading.');
    const git = (...args) => {
        const result = spawnSync('git', args, { cwd: root, encoding: 'utf8' });
        if (result.status !== 0) throw new Error(result.stderr || `git ${args.join(' ')} failed`);
        return result.stdout.trim();
    };
    const report = {
        revision: git('rev-parse', 'HEAD'), dirty: !!git('status', '--porcelain'),
        fixture: 'website-v1', platform: values.platform, theme: values.theme,
        capturedAt: new Date().toISOString(), requestedDimensions: { width, height },
        pixelRatio, pixelDimensions: { width: width * pixelRatio, height: height * pixelRatio }, shots: []
    };
    const flow = new DevFlow(values['agent-port'], Math.max(timeout, 30000));
    const verify = state => {
        if (state.diagnostics?.fixture !== report.fixture || state.diagnostics?.platform !== values.platform)
            throw new Error(`Not the requested Screenshot Mode app: ${JSON.stringify(state.diagnostics)}`);
        if (state.diagnostics.violations !== '0' || state.errors.length || state.remoteResources.length || state.blurred || state.debugMenu || state.sidebarOverflow)
            throw new Error(`Unsafe capture state: ${JSON.stringify(state)}`);
        if (state.diagnostics.interrupted === 'true')
            throw new Error('The capture window lost focus. This set is interrupted; rerun it after the user is ready.');
    };
    const waitFor = async (predicate, label) => {
        const deadline = Date.now() + timeout;
        let previous, state;
        while (Date.now() < deadline) {
            state = JSON.parse(await flow.evaluate(stateExpression));
            verify(state);
            if (state.diagnostics.ready === 'true' && state.loading === 0 && predicate(state) && previous === state.fingerprint)
                return state;
            previous = state.fingerprint;
            await delay(200);
        }
        throw new Error(`Timed out waiting for ${label}: ${JSON.stringify(state)}`);
    };
    const getCaptureProcessId = async () => {
        const response = await fetch(`http://localhost:${values['agent-port']}/api/v1/agent/status`, { signal: AbortSignal.timeout(timeout) });
        if (!response.ok) throw new Error(`Agent status failed (${response.status}).`);
        const processId = (await response.json()).app?.processId;
        if (!Number.isInteger(processId))
            throw new Error('The DevFlow agent did not report the capture process ID.');
        return processId;
    };
    let ownsCapture = false;
    let captureProcessId;
    try {
        await waitFor(() => true, 'Screenshot Mode initialization');
        await flow.command(`MAUI resize ${width} ${height}`);
        if (values.platform === 'windows') {
            captureProcessId = await getCaptureProcessId();
            const result = spawnSync('python', [
                join(root, 'build', 'capture-windows-window.py'), '--pid', String(captureProcessId),
                '--prepare', '--width', String(report.pixelDimensions.width),
                '--height', String(report.pixelDimensions.height), '--pixel-ratio', String(pixelRatio)
            ], { encoding: 'utf8' });
            if (result.status !== 0) throw new Error(result.stderr || result.error?.message || 'Windows window preparation failed');
        }
        if (!values['check-only']) {
            if (!values.confirmed) {
                if (!process.stdin.isTTY)
                    throw new Error('The app is ready. Ask the user to leave it untouched until capture finishes, then rerun with --confirmed only after they agree.');
                const prompt = createPrompt({ input: process.stdin, output: process.stdout });
                try {
                    const answer = await prompt.question('The app is ready. Leave the focused window untouched until the full set finishes; it will close afterward. Ready? [yes/no] ');
                    if (answer.trim().toLowerCase() !== 'yes')
                        throw new Error('Capture cancelled; no screenshots were taken.');
                } finally {
                    prompt.close();
                }
            }
            ownsCapture = true;
            captureProcessId ??= await getCaptureProcessId();
            console.log('Starting in 5 seconds. Please leave the app untouched until it closes.');
            await delay(5000);
            await flow.evaluate('window.sherpaScreenshot.beginCapture().then(()=>true)');
        }
        const screenshot = async path => {
            if (values.platform === 'macos') {
                await flow.command(`MAUI screenshot --scale native --output "${path}" --overwrite`);
                return;
            }
            await delay(500);
            const windows = values.platform === 'windows';
            const result = spawnSync(windows ? 'python' : 'python3', [
                join(root, 'build', windows ? 'capture-windows-window.py' : 'capture-gtk-window.py'),
                '--pid', String(captureProcessId), '--output', path
            ], { encoding: 'utf8' });
            if (result.status !== 0) throw new Error(result.stderr || result.error?.message || 'Native window capture failed');
        };
        for (const theme of values.theme === 'both' ? ['dark', 'light'] : [values.theme]) {
            const directory = join(resolve(values.output), values.platform, theme);
            mkdirSync(directory, { recursive: true });
            report.theme = theme;
            report.shots = [];
            await flow.evaluate(`window.sherpaScreenshot.setAppearance('${theme}').then(()=>true)`);
            await waitFor(state => state.diagnostics.theme === theme, 'native and web appearance');
            for (const shot of shots) {
                await flow.evaluate(`(()=>{const a=document.createElement('a');a.href='/${shot.route}';document.body.appendChild(a);a.click();a.remove();return true})()`);
                let state = await waitFor(
                    state => state.route.replace(/\/$/, '') === `/${shot.route}`.replace(/\/$/, '')
                        && state.title?.includes(headings[shot.id])
                        && (shot.id !== 'android-sdk' || state.tabs.some(tab => tab.startsWith('Installed ('))),
                    shot.id);
                if (shot.id === 'android-sdk') {
                    const clicked = JSON.parse(await flow.evaluate(`(()=>{const tab=Array.from(document.querySelectorAll('.tabs .tab')).find(e=>e.textContent.trim().startsWith('Installed ('));if(tab)tab.click();return JSON.stringify({clicked:!!tab})})()`));
                    if (!clicked.clicked)
                        throw new Error('The Android SDK Installed tab is unavailable.');
                    state = await waitFor(state => state.activeTab?.startsWith('Installed ('), 'Android SDK Installed tab');
                }
                await flow.evaluate(`document.querySelector('main.content')?.scrollTo(0,0)`);
                const entry = { id: shot.id, route: shot.route, heading: state.title, diagnostics: state.diagnostics };
                if (shot.id === 'android-sdk') entry.activeTab = state.activeTab;
                if (!values['check-only']) {
                    const path = join(directory, `${shot.id}.png`);
                    await screenshot(path);
                    const image = readFileSync(path);
                    if (image.subarray(0, 8).toString('hex') !== '89504e470d0a1a0a') throw new Error(`Not a PNG: ${path}`);
                    entry.width = image.readUInt32BE(16);
                    entry.height = image.readUInt32BE(20);
                    if (entry.width !== report.pixelDimensions.width || entry.height !== report.pixelDimensions.height)
                        throw new Error(`Native screenshot is ${entry.width} x ${entry.height}, not ${report.pixelDimensions.width} x ${report.pixelDimensions.height} (${width} x ${height} at ${pixelRatio}x). Use the requested display scale; do not upscale the image.`);
                    entry.file = `${shot.id}.png`;
                }
                verify(JSON.parse(await flow.evaluate(stateExpression)));
                report.shots.push(entry);
                writeFileSync(join(directory, 'capture.json'), JSON.stringify(report, null, 2) + '\n');
                console.log(`${values['check-only'] ? 'Checked' : 'Captured'} ${theme}/${shot.id}: ${state.title}`);
            }
        }
    } finally {
        try {
            if (ownsCapture) await flow.evaluate('window.sherpaScreenshot.closeCapture().then(()=>true)');
        } finally {
            await flow.close();
        }
        if (ownsCapture && captureProcessId) {
            const deadline = Date.now() + 10000;
            let running = true;
            while (running && Date.now() < deadline) {
                try {
                    process.kill(captureProcessId, 0);
                    await delay(100);
                } catch (error) {
                    if (error.code !== 'ESRCH') throw error;
                    running = false;
                }
            }
            if (running) throw new Error(`The isolated capture app (${captureProcessId}) did not exit after closing.`);
            console.log('The isolated capture app has closed.');
        }
    }
}

main().catch(error => {
    console.error(error.message);
    process.exitCode = 1;
});

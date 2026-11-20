import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { createServer } from 'node:http';
import { mkdtemp, mkdir, writeFile, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { delimiter, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../', import.meta.url));

test('capture runner requires consent, preserves native pixels, and closes confirmed runs', { skip: process.platform === 'win32' }, async t => {
    for (const scenario of ['confirmed', 'unconfirmed', 'low-dpi']) {
        await t.test(scenario, async () => {
            const directory = await mkdtemp(join(process.env.MAUI_SHERPA_TEST_ARTIFACTS || tmpdir(), 'sherpa-capture-'));
            const app = spawn(process.execPath, ['-e', 'setInterval(() => {}, 1000)'], { stdio: 'ignore' });
            const exited = once(app, 'exit');
            const server = createServer((request, response) => {
                response.setHeader('Content-Type', 'application/json');
                response.end(JSON.stringify({ app: { processId: app.pid } }));
            });
            try {
                const bin = join(directory, 'bin');
                await mkdir(bin);
                await writeFile(join(bin, 'dotnet'), `#!/usr/bin/env node
const { createInterface } = require('node:readline');
const { appendFileSync, writeFileSync } = require('node:fs');
const state = {
    diagnostics: {fixture:'website-v1',platform:'macos',ready:'true',theme:'dark',violations:'0',interrupted:'false'},
    route:'/',title:'Dashboard',loading:0,errors:[],remoteResources:[],blurred:false,debugMenu:false,sidebarOverflow:false,fingerprint:'settled'
};
createInterface({input:process.stdin}).on('line', command => {
    appendFileSync(process.env.MOCK_LOG, command + '\\n');
    let output = 'true';
    if (command.includes('JSON.stringify')) output = JSON.stringify(state);
    if (command.startsWith('MAUI screenshot')) {
        const path = command.match(/--output "([^"]+)"/)[1];
        const image = Buffer.alloc(24);
        Buffer.from('89504e470d0a1a0a','hex').copy(image);
        image.writeUInt32BE(Number(process.env.MOCK_WIDTH),16);
        image.writeUInt32BE(Number(process.env.MOCK_WIDTH)*2/3,20);
        writeFileSync(path,image);
    }
    if (command.includes('closeCapture')) process.kill(Number(process.env.MOCK_PID),'SIGTERM');
    console.log(JSON.stringify({command,exit_code:0,output}));
});
`, { mode: 0o755 });
                server.listen(0, '127.0.0.1');
                await once(server, 'listening');
                const log = join(directory, 'commands.log');
                const child = spawn(process.execPath, [
                    'build/capture-screenshots.mjs', '--agent-port', String(server.address().port),
                    '--platform', 'macos', '--shots', 'dashboard', '--output', directory,
                    ...(scenario === 'unconfirmed' ? [] : ['--confirmed'])
                ], {
                    cwd: root,
                    env: {
                        ...process.env, PATH: `${bin}${delimiter}${process.env.PATH}`,
                        MOCK_LOG: log, MOCK_PID: String(app.pid),
                        MOCK_WIDTH: scenario === 'low-dpi' ? '1440' : '2880'
                    },
                    stdio: ['ignore', 'pipe', 'pipe']
                });
                let stdout = '', stderr = '';
                child.stdout.on('data', chunk => { stdout += chunk; });
                child.stderr.on('data', chunk => { stderr += chunk; });
                const started = Date.now();
                const [code] = await once(child, 'exit');
                const commands = await readFile(log, 'utf8');
                if (scenario === 'unconfirmed') {
                    assert.equal(code, 1);
                    assert.match(stderr, /Ask the user/);
                    assert.doesNotMatch(commands, /MAUI screenshot|beginCapture|closeCapture/);
                    return;
                }
                assert.ok(Date.now() - started >= 5000, 'The user gets the full five-second countdown');
                assert.match(commands, /MAUI screenshot --scale native/);
                assert.match(commands, /beginCapture/);
                assert.match(commands, /closeCapture/);
                await exited;
                assert.match(stdout, /isolated capture app has closed/);
                if (scenario === 'low-dpi') {
                    assert.equal(code, 1);
                    assert.match(stderr, /do not upscale/);
                } else {
                    assert.equal(code, 0, stderr);
                    const report = JSON.parse(await readFile(join(directory, 'macos/dark/capture.json'), 'utf8'));
                    assert.deepEqual(report.pixelDimensions, { width: 2880, height: 1920 });
                    assert.deepEqual(report.requestedDimensions, { width: 1440, height: 960 });
                    assert.equal(report.shots[0].width, 2880);
                }
            } finally {
                app.kill();
                await exited;
                server.close();
                await rm(directory, { recursive: true, force: true });
            }
        });
    }
});

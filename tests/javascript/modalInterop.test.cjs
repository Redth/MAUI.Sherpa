const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

const source = fs.readFileSync(
    path.join(__dirname, '../../src/MauiSherpa/wwwroot/js/modalInterop.js'), 'utf8');

function setup(nativeActions, count = 3) {
    const calls = [];
    const elements = Array.from({ length: count }, () => ({
        offsetParent: {},
        focus() { document.activeElement = this; }
    }));
    const listeners = new Map();
    const modal = {
        querySelectorAll: () => elements,
        querySelector: () => null,
        addEventListener: (name, callback) => listeners.set(name, callback),
        removeEventListener: (name) => listeners.delete(name)
    };
    const document = { getElementById: () => modal, activeElement: null };
    const window = {};
    vm.runInNewContext(source, { window, document, requestAnimationFrame: fn => fn() });
    window.modalInterop.initialize('pairing', {
        invokeMethodAsync: (...args) => { calls.push(args); return Promise.resolve(); }
    }, nativeActions);
    return {
        elements, calls, document, window, listeners,
        press(key, shiftKey = false) {
            const event = {
                key, shiftKey, prevented: false,
                preventDefault() { this.prevented = true; },
                stopPropagation() {}
            };
            listeners.get('keydown')(event);
            return event;
        }
    };
}

test('ordinary web modals continue to wrap focus in both directions', () => {
    const ui = setup(false);
    ui.document.activeElement = ui.elements.at(-1);
    assert.equal(ui.press('Tab').prevented, true);
    assert.equal(ui.document.activeElement, ui.elements[0]);
    ui.press('Tab', true);
    assert.equal(ui.document.activeElement, ui.elements.at(-1));
    assert.deepEqual(ui.calls, []);
});

test('hybrid forms hand forward focus to native actions at the last control', () => {
    const ui = setup(true);
    ui.document.activeElement = ui.elements.at(-1);
    assert.equal(ui.press('Tab').prevented, true);
    assert.deepEqual(ui.calls, [['OnFocusBoundary', false]]);
});

test('hybrid forms hand backward focus to native actions at the first control', () => {
    const ui = setup(true);
    ui.document.activeElement = ui.elements[0];
    assert.equal(ui.press('Tab', true).prevented, true);
    assert.deepEqual(ui.calls, [['OnFocusBoundary', true]]);
});

test('hybrid forms still move focus between web controls explicitly', () => {
    const ui = setup(true);
    ui.document.activeElement = ui.elements[0];
    ui.press('Tab');
    assert.equal(ui.document.activeElement, ui.elements[1]);
    ui.press('Tab', true);
    assert.equal(ui.document.activeElement, ui.elements[0]);
    assert.deepEqual(ui.calls, []);
});

test('a busy hybrid form with no web controls can still reach native Stop', () => {
    const ui = setup(true, 0);
    assert.equal(ui.press('Tab').prevented, true);
    assert.deepEqual(ui.calls, [['OnFocusBoundary', false]]);
});

test('Escape invokes the component cancellation and close handler', () => {
    const ui = setup(true);
    assert.equal(ui.press('Escape').prevented, true);
    assert.deepEqual(ui.calls, [['OnEscapePressed']]);
});

test('hybrid forms leave initial focus with the native window', () => {
    assert.equal(setup(true).document.activeElement, null);
    const web = setup(false);
    assert.equal(web.document.activeElement, web.elements[0]);
});

test('dispose removes the keyboard listener and its reference', () => {
    const ui = setup(true);
    ui.window.modalInterop.dispose('pairing');
    assert.equal(ui.listeners.size, 0);
    assert.equal(ui.window.modalInterop._instances.size, 0);
});

// Terminal.js - xterm.js interop for Blazor
window.terminalInterop = {
    terminals: {},

    /**
     * Initialize a terminal in the specified container
     */
    initialize: function (containerId, options) {
        const container = document.getElementById(containerId);
        if (!container) {
            console.error('Terminal container not found:', containerId);
            return false;
        }

        // Default options
        const termOptions = {
            cursorBlink: false,
            disableStdin: true,
            fontSize: 13,
            fontFamily: '"SF Mono", Menlo, Monaco, "Courier New", monospace',
            theme: {
                background: '#1e1e1e',
                foreground: '#d4d4d4',
                cursor: '#d4d4d4',
                cursorAccent: '#1e1e1e',
                black: '#1e1e1e',
                red: '#f44747',
                green: '#6a9955',
                yellow: '#dcdcaa',
                blue: '#569cd6',
                magenta: '#c586c0',
                cyan: '#4ec9b0',
                white: '#d4d4d4',
                brightBlack: '#808080',
                brightRed: '#f44747',
                brightGreen: '#6a9955',
                brightYellow: '#dcdcaa',
                brightBlue: '#569cd6',
                brightMagenta: '#c586c0',
                brightCyan: '#4ec9b0',
                brightWhite: '#ffffff'
            },
            scrollback: 5000,
            convertEol: true,
            ...options
        };

        const terminal = new Terminal(termOptions);
        const fitAddon = new FitAddon.FitAddon();
        terminal.loadAddon(fitAddon);

        terminal.open(container);
        fitAddon.fit();

        // Store reference
        this.terminals[containerId] = {
            terminal: terminal,
            fitAddon: fitAddon,
            autoScroll: true
        };

        // Handle resize
        const resizeObserver = new ResizeObserver(() => {
            try {
                fitAddon.fit();
            } catch (e) {
                // Ignore resize errors
            }
        });
        resizeObserver.observe(container);

        // Track user scroll to disable auto-scroll
        terminal.element.addEventListener('wheel', () => {
            const t = this.terminals[containerId];
            if (t) {
                // If user scrolls up, disable auto-scroll
                const buffer = terminal.buffer.active;
                const isAtBottom = buffer.baseY + buffer.viewportY >= buffer.length - terminal.rows;
                t.autoScroll = isAtBottom;
            }
        });

        return true;
    },

    /**
     * Write text to the terminal
     */
    write: function (containerId, text) {
        const t = this.terminals[containerId];
        if (!t) return;

        t.terminal.write(text + '\r\n');

        // Auto-scroll if enabled
        if (t.autoScroll) {
            t.terminal.scrollToBottom();
        }
    },

    /**
     * Write error text (in red) to the terminal
     */
    writeError: function (containerId, text) {
        const t = this.terminals[containerId];
        if (!t) return;

        // ANSI red color code
        t.terminal.write('\x1b[31m' + text + '\x1b[0m\r\n');

        if (t.autoScroll) {
            t.terminal.scrollToBottom();
        }
    },

    /**
     * Write success text (in green) to the terminal
     */
    writeSuccess: function (containerId, text) {
        const t = this.terminals[containerId];
        if (!t) return;

        // ANSI green color code
        t.terminal.write('\x1b[32m' + text + '\x1b[0m\r\n');

        if (t.autoScroll) {
            t.terminal.scrollToBottom();
        }
    },

    /**
     * Write warning text (in yellow) to the terminal
     */
    writeWarning: function (containerId, text) {
        const t = this.terminals[containerId];
        if (!t) return;

        // ANSI yellow color code
        t.terminal.write('\x1b[33m' + text + '\x1b[0m\r\n');

        if (t.autoScroll) {
            t.terminal.scrollToBottom();
        }
    },

    /**
     * Write command text (in cyan) to the terminal
     */
    writeCommand: function (containerId, text) {
        const t = this.terminals[containerId];
        if (!t) return;

        // ANSI cyan color code
        t.terminal.write('\x1b[36m' + text + '\x1b[0m\r\n');

        if (t.autoScroll) {
            t.terminal.scrollToBottom();
        }
    },

    /**
     * Clear the terminal
     */
    clear: function (containerId) {
        const t = this.terminals[containerId];
        if (!t) return;

        t.terminal.clear();
    },

    /**
     * Scroll to bottom
     */
    scrollToBottom: function (containerId) {
        const t = this.terminals[containerId];
        if (!t) return;

        t.terminal.scrollToBottom();
        t.autoScroll = true;
    },

    /**
     * Enable/disable auto-scroll
     */
    setAutoScroll: function (containerId, enabled) {
        const t = this.terminals[containerId];
        if (!t) return;

        t.autoScroll = enabled;
        if (enabled) {
            t.terminal.scrollToBottom();
        }
    },

    /**
     * Get all terminal content as text
     */
    getContent: function (containerId) {
        const t = this.terminals[containerId];
        if (!t) return '';

        const buffer = t.terminal.buffer.active;
        let content = '';
        for (let i = 0; i < buffer.length; i++) {
            const line = buffer.getLine(i);
            if (line) {
                content += line.translateToString(true) + '\n';
            }
        }
        return content.trim();
    },

    /**
     * Fit terminal to container
     */
    fit: function (containerId) {
        const t = this.terminals[containerId];
        if (!t) return;

        t.fitAddon.fit();
    },

    /**
     * Initialize an interactive terminal with local line editing and command forwarding
     */
    initializeInteractive: function (containerId, dotnetRef, options) {
        const container = document.getElementById(containerId);
        if (!container) {
            console.error('Terminal container not found:', containerId);
            return false;
        }

        const termOptions = {
            cursorBlink: true,
            disableStdin: false,
            fontSize: 13,
            fontFamily: '"SF Mono", Menlo, Monaco, "Courier New", monospace',
            theme: {
                background: '#1a1a2e',
                foreground: '#e0e0e0',
                cursor: '#3ddc84',
                cursorAccent: '#1a1a2e',
                selectionBackground: 'rgba(66, 153, 225, 0.3)',
                black: '#1a1a2e',
                red: '#f44747',
                green: '#3ddc84',
                yellow: '#dcdcaa',
                blue: '#569cd6',
                magenta: '#c586c0',
                cyan: '#4ec9b0',
                white: '#e0e0e0',
                brightBlack: '#808080',
                brightRed: '#f44747',
                brightGreen: '#3ddc84',
                brightYellow: '#dcdcaa',
                brightBlue: '#569cd6',
                brightMagenta: '#c586c0',
                brightCyan: '#4ec9b0',
                brightWhite: '#ffffff'
            },
            scrollback: 10000,
            convertEol: true,
            ...options
        };

        const terminal = new Terminal(termOptions);
        const fitAddon = new FitAddon.FitAddon();
        terminal.loadAddon(fitAddon);

        terminal.open(container);
        fitAddon.fit();

        const state = {
            terminal: terminal,
            fitAddon: fitAddon,
            autoScroll: true,
            dotnetRef: dotnetRef,
            currentLine: '',
            cursorPos: 0,
            history: [],
            historyIndex: -1,
            promptWritten: false
        };

        this.terminals[containerId] = state;

        // Local line editing — we handle echo, backspace, arrows, etc.
        terminal.onData(data => {
            const s = this.terminals[containerId];
            if (!s) return;

            for (let i = 0; i < data.length; i++) {
                const ch = data[i];
                const code = ch.charCodeAt(0);

                if (ch === '\r') {
                    // Enter — send command
                    terminal.write('\r\n');
                    const cmd = s.currentLine;
                    s.currentLine = '';
                    s.cursorPos = 0;
                    if (cmd.trim().length > 0) {
                        s.history.push(cmd);
                        if (s.history.length > 200) s.history.shift();
                    }
                    s.historyIndex = s.history.length;
                    s.promptWritten = false;
                    dotnetRef.invokeMethodAsync('OnTerminalData', cmd);
                } else if (code === 127 || code === 8) {
                    // Backspace
                    if (s.cursorPos > 0) {
                        const before = s.currentLine.slice(0, s.cursorPos - 1);
                        const after = s.currentLine.slice(s.cursorPos);
                        s.currentLine = before + after;
                        s.cursorPos--;
                        // Move cursor back, rewrite rest of line, clear trailing char
                        terminal.write('\b' + after + ' ' + '\b'.repeat(after.length + 1));
                    }
                } else if (ch === '\x1b' && data[i+1] === '[') {
                    // Arrow key sequences
                    const arrow = data[i+2];
                    i += 2;
                    if (arrow === 'A') {
                        // Up — history back
                        if (s.historyIndex > 0) {
                            this._clearInput(s);
                            s.historyIndex--;
                            s.currentLine = s.history[s.historyIndex];
                            s.cursorPos = s.currentLine.length;
                            terminal.write(s.currentLine);
                        }
                    } else if (arrow === 'B') {
                        // Down — history forward
                        this._clearInput(s);
                        if (s.historyIndex < s.history.length - 1) {
                            s.historyIndex++;
                            s.currentLine = s.history[s.historyIndex];
                        } else {
                            s.historyIndex = s.history.length;
                            s.currentLine = '';
                        }
                        s.cursorPos = s.currentLine.length;
                        terminal.write(s.currentLine);
                    } else if (arrow === 'C') {
                        // Right
                        if (s.cursorPos < s.currentLine.length) {
                            s.cursorPos++;
                            terminal.write('\x1b[C');
                        }
                    } else if (arrow === 'D') {
                        // Left
                        if (s.cursorPos > 0) {
                            s.cursorPos--;
                            terminal.write('\x1b[D');
                        }
                    }
                } else if (code === 3) {
                    // Ctrl+C — cancel current line
                    terminal.write('^C\r\n');
                    s.currentLine = '';
                    s.cursorPos = 0;
                    s.promptWritten = false;
                    this._writePrompt(s);
                } else if (code === 21) {
                    // Ctrl+U — clear line
                    this._clearInput(s);
                    s.currentLine = '';
                    s.cursorPos = 0;
                } else if (code >= 32) {
                    // Printable character
                    const before = s.currentLine.slice(0, s.cursorPos);
                    const after = s.currentLine.slice(s.cursorPos);
                    s.currentLine = before + ch + after;
                    s.cursorPos++;
                    terminal.write(ch + after);
                    if (after.length > 0) {
                        terminal.write('\b'.repeat(after.length));
                    }
                }
            }
        });

        const resizeObserver = new ResizeObserver(() => {
            try { fitAddon.fit(); } catch (e) { }
        });
        resizeObserver.observe(container);

        // Focus terminal and write initial prompt
        terminal.focus();
        this._writePrompt(state);

        return true;
    },

    /** Write the shell prompt */
    _writePrompt: function (state) {
        if (state.promptWritten) return;
        state.terminal.write('\x1b[36m❯\x1b[0m ');
        state.promptWritten = true;
    },

    /** Clear the current input text from the terminal display */
    _clearInput: function (state) {
        // Move cursor to start of input, overwrite with spaces, move back
        if (state.cursorPos > 0) {
            state.terminal.write('\b'.repeat(state.cursorPos));
        }
        state.terminal.write(' '.repeat(state.currentLine.length));
        state.terminal.write('\b'.repeat(state.currentLine.length));
    },

    /**
     * Write raw data to the terminal (for command output), then show prompt
     */
    writeRaw: function (containerId, data) {
        const t = this.terminals[containerId];
        if (!t) return;
        t.terminal.write(data);
    },

    /**
     * Write output and show prompt when command is done
     */
    writeOutput: function (containerId, data) {
        const t = this.terminals[containerId];
        if (!t) return;
        t.terminal.write(data);
        t.promptWritten = false;
        this._writePrompt(t);
    },

    /**
     * Focus the terminal
     */
    focus: function (containerId) {
        const t = this.terminals[containerId];
        if (!t) return;
        t.terminal.focus();
    },

    /**
     * Write the prompt if not already shown
     */
    writePrompt: function (containerId) {
        const t = this.terminals[containerId];
        if (!t) return;
        t.promptWritten = false;
        this._writePrompt(t);
    },

    /**
     * Dispose of the terminal
     */
    dispose: function (containerId) {
        const t = this.terminals[containerId];
        if (!t) return;

        t.terminal.dispose();
        delete this.terminals[containerId];
    }
};

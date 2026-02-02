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
     * Dispose of the terminal
     */
    dispose: function (containerId) {
        const t = this.terminals[containerId];
        if (!t) return;

        t.terminal.dispose();
        delete this.terminals[containerId];
    }
};

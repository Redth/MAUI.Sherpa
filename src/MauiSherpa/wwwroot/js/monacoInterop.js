// Hosts the vendored Monaco editor (build/monaco) for JSON payload editing.
// Monaco is loaded on first use so pages that never show an editor don't pay for it.
(function () {
    const base = 'vendor/monaco/';
    const editors = new Map();
    let loading;

    const str = (description, extra) => ({ type: 'string', description, ...extra });
    const int = (description, extra) => ({ type: 'integer', description, ...extra });
    const obj = (description, properties, extra) => ({ type: 'object', description, properties, ...extra });
    const strMap = description => ({ type: 'object', description, additionalProperties: { type: 'string' } });

    const apnsAlert = obj('Alert content shown to the user.', {
        title: str('Short title shown above the body.'),
        subtitle: str('Secondary title shown below the title.'),
        body: str('Message text.'),
        'launch-image': str('Image file shown while the app launches from the notification.'),
        'title-loc-key': str('Localization key for the title.'),
        'title-loc-args': { type: 'array', items: { type: 'string' }, description: 'Replacement values for the title localization key.' },
        'subtitle-loc-key': str('Localization key for the subtitle.'),
        'subtitle-loc-args': { type: 'array', items: { type: 'string' }, description: 'Replacement values for the subtitle localization key.' },
        'loc-key': str('Localization key for the body.'),
        'loc-args': { type: 'array', items: { type: 'string' }, description: 'Replacement values for the body localization key.' }
    });

    const apnsSchema = {
        type: 'object',
        description: 'Apple Push Notification service payload.',
        properties: {
            aps: obj('Apple-defined keys that tell the system how to deliver the notification.', {
                alert: { description: 'Alert text, or an object with title, subtitle, and body.', anyOf: [{ type: 'string' }, apnsAlert] },
                badge: int('Number shown on the app icon. Use 0 to remove the badge.', { minimum: 0 }),
                sound: {
                    description: 'Sound to play: "default", a bundled file name, or a critical-alert dictionary.',
                    anyOf: [
                        { type: 'string', enum: ['default'] },
                        { type: 'string' },
                        obj('Critical alert sound.', {
                            critical: { type: 'integer', enum: [0, 1], description: '1 marks the sound as a critical alert.' },
                            name: str('Sound file name, or "default".'),
                            volume: { type: 'number', minimum: 0, maximum: 1, description: 'Volume from 0.0 to 1.0.' }
                        })
                    ]
                },
                'thread-id': str('Groups related notifications together.'),
                category: str('Notification category that defines actions shown with the notification.'),
                'content-available': { type: 'integer', enum: [1], description: 'Set to 1 for a silent background update.' },
                'mutable-content': { type: 'integer', enum: [1], description: 'Set to 1 to let a notification service extension modify the content.' },
                'target-content-id': str('Window or content to bring forward when the notification opens.'),
                'interruption-level': str('How urgently the system presents the notification.', { enum: ['passive', 'active', 'time-sensitive', 'critical'] }),
                'relevance-score': { type: 'number', minimum: 0, maximum: 1, description: 'Ranks notifications in a summary, from 0.0 to 1.0.' },
                'filter-criteria': str('Focus filter criteria used to show or hide the notification.'),
                'stale-date': int('UNIX time when a Live Activity becomes stale.'),
                'content-state': { type: 'object', description: 'Updated Live Activity content state.' },
                timestamp: int('UNIX time of a Live Activity update.'),
                event: str('Live Activity event.', { enum: ['start', 'update', 'end'] }),
                'dismissal-date': int('UNIX time when an ended Live Activity is removed.'),
                'attributes-type': str('Live Activity attributes type name, for push-to-start.'),
                attributes: { type: 'object', description: 'Live Activity attributes, for push-to-start.' }
            })
        },
        required: ['aps']
    };

    const fcmNotification = obj('Basic notification shown on every platform.', {
        title: str('Notification title.'),
        body: str('Notification body text.'),
        image: str('URL of an image to show in the notification.', { format: 'uri' })
    });

    const fcmMessage = obj('FCM HTTP v1 message.', {
        token: str('Registration token of the target device.'),
        topic: str('Topic name, without the "/topics/" prefix.'),
        condition: str('Topic condition, for example "\'dogs\' in topics && \'cats\' in topics".'),
        notification: fcmNotification,
        data: strMap('Custom key/value pairs delivered to the app. Values must be strings.'),
        fcm_options: obj('Options shared by all platforms.', { analytics_label: str('Label for message analytics.') }),
        android: obj('Android-specific options.', {
            collapse_key: str('Groups messages so only the latest is delivered when the device comes online.'),
            priority: str('Delivery priority.', { enum: ['NORMAL', 'HIGH'] }),
            ttl: str('How long to keep the message if the device is offline, for example "3600s".'),
            restricted_package_name: str('Package name the registration token must match.'),
            data: strMap('Android data payload; overrides the top-level data.'),
            direct_boot_ok: { type: 'boolean', description: 'Deliver while the device is in direct boot mode.' },
            notification: obj('Android notification options.', {
                title: str('Notification title.'),
                body: str('Notification body text.'),
                icon: str('Drawable resource name for the icon.'),
                color: str('Icon color in #rrggbb format.'),
                sound: str('Sound to play, or "default".'),
                tag: str('Replaces an existing notification with the same tag.'),
                click_action: str('Intent action launched when the notification is tapped.'),
                channel_id: str('Notification channel ID (Android 8.0+).'),
                image: str('URL of an image to show in the notification.'),
                notification_priority: str('Notification priority.', { enum: ['PRIORITY_UNSPECIFIED', 'PRIORITY_MIN', 'PRIORITY_LOW', 'PRIORITY_DEFAULT', 'PRIORITY_HIGH', 'PRIORITY_MAX'] }),
                visibility: str('Lock screen visibility.', { enum: ['VISIBILITY_UNSPECIFIED', 'PRIVATE', 'PUBLIC', 'SECRET'] }),
                notification_count: int('Number of items this notification represents.'),
                default_sound: { type: 'boolean', description: 'Use the default notification sound.' },
                default_vibrate_timings: { type: 'boolean', description: 'Use the default vibration pattern.' },
                sticky: { type: 'boolean', description: 'Keep the notification after the user taps it.' }
            })
        }),
        apns: obj('APNs options for Apple devices.', {
            headers: strMap('APNs request headers, such as apns-priority or apns-push-type.'),
            payload: apnsSchema,
            fcm_options: obj('APNs FCM options.', {
                analytics_label: str('Label for message analytics.'),
                image: str('URL of an image to show in the notification.')
            })
        }),
        webpush: obj('Web push options.', {
            headers: strMap('Web push headers, such as TTL.'),
            data: strMap('Web push data payload.'),
            notification: { type: 'object', description: 'Web Notification API options.' },
            fcm_options: obj('Web push FCM options.', { link: str('Link to open when the notification is clicked.'), analytics_label: str('Label for message analytics.') })
        })
    });

    const fcmSchema = {
        description: 'An FCM v1 message, with or without the {"message": …} wrapper.',
        anyOf: [obj('Request body.', { message: fcmMessage, validate_only: { type: 'boolean', description: 'Validate without delivering.' } }, { required: ['message'] }), fcmMessage]
    };

    const googleServiceAccountSchema = {
        type: 'object',
        description: 'Google Cloud / Firebase service account credentials JSON.',
        properties: {
            type: str('Credential type, typically "service_account".', { enum: ['service_account'] }),
            project_id: str('Google Cloud or Firebase project ID.'),
            private_key_id: str('Unique identifier for the private key.'),
            private_key: str('PEM-encoded RSA private key.'),
            client_email: str('Service account email address.'),
            client_id: str('Numeric client ID of the service account.'),
            auth_uri: str('OAuth 2.0 authorization server URI.', { format: 'uri' }),
            token_uri: str('OAuth 2.0 token server URI.', { format: 'uri' }),
            auth_provider_x509_cert_url: str('URL of the public x509 certificate for the auth provider.', { format: 'uri' }),
            client_x509_cert_url: str('URL of the public x509 certificate for the client.', { format: 'uri' }),
            universe_domain: str('Universe domain, typically "googleapis.com".')
        },
        required: ['type', 'project_id', 'private_key', 'client_email']
    };

    const schemas = {
        apns: { uri: 'https://mauisherpa.local/schemas/apns-payload.json', schema: apnsSchema },
        fcm: { uri: 'https://mauisherpa.local/schemas/fcm-message.json', schema: fcmSchema },
        'google-service-account': { uri: 'https://mauisherpa.local/schemas/google-service-account.json', schema: googleServiceAccountSchema }
    };

    function loadScript(src) {
        return new Promise((resolve, reject) => {
            const s = document.createElement('script');
            s.src = src;
            s.onload = resolve;
            s.onerror = () => reject(new Error('Failed to load ' + src));
            document.head.appendChild(s);
        });
    }

    async function createWorker(file) {
        const url = new URL(base + file, document.baseURI).href;
        try {
            return new Worker(url);
        } catch {
            // Some WebViews refuse workers from the app's custom scheme; a blob URL is always same-origin.
            const code = await (await fetch(url)).text();
            return new Worker(URL.createObjectURL(new Blob([code], { type: 'text/javascript' })));
        }
    }

    // Monaco themes need hex colors, so resolve the console tokens through the browser.
    function cssColor(name, fallback) {
        const probe = document.createElement('span');
        probe.style.color = 'var(' + name + ', ' + fallback + ')';
        probe.style.display = 'none';
        document.body.appendChild(probe);
        const value = getComputedStyle(probe).color;
        probe.remove();
        const m = value.match(/[\d.]+/g);
        if (!m || m.length < 3) return fallback;
        const hex = n => Math.round(Number(n)).toString(16).padStart(2, '0');
        return '#' + hex(m[0]) + hex(m[1]) + hex(m[2]) + (m.length > 3 ? hex(Number(m[3]) * 255) : '');
    }

    function defineTheme() {
        const c = name => cssColor(name, '#888888').slice(1, 7);
        const accent = cssColor('--accent', '#3b82f6');
        const text = cssColor('--console-text', '#e4e4e7');
        const muted = cssColor('--console-muted', '#8e8e98');
        monaco.editor.defineTheme('sherpa-console', {
            base: 'vs-dark',
            inherit: true,
            rules: [
                { token: '', foreground: c('--console-text') },
                { token: 'string.key.json', foreground: c('--console-cmd') },
                { token: 'string.value.json', foreground: c('--console-success') },
                { token: 'number.json', foreground: c('--console-warning') },
                { token: 'keyword.json', foreground: c('--console-info') },
                { token: 'delimiter', foreground: c('--console-muted') },
                { token: 'comment', foreground: c('--console-muted'), fontStyle: 'italic' }
            ],
            colors: {
                'editor.background': cssColor('--surface-console', '#141416'),
                'editor.foreground': text,
                'editorLineNumber.foreground': muted + '99',
                'editorLineNumber.activeForeground': text,
                'editor.lineHighlightBackground': '#ffffff0a',
                'editor.lineHighlightBorder': '#00000000',
                'editorCursor.foreground': accent,
                'editor.selectionBackground': accent.slice(0, 7) + '55',
                'editorIndentGuide.background1': '#ffffff12',
                'editorBracketMatch.border': accent.slice(0, 7) + '99',
                'editorWidget.background': cssColor('--surface-console-header', '#1c1c1f'),
                'editorSuggestWidget.background': cssColor('--surface-console-header', '#1c1c1f'),
                'editorSuggestWidget.selectedBackground': accent.slice(0, 7) + '40',
                'editorHoverWidget.background': cssColor('--surface-console-header', '#1c1c1f'),
                'editorError.foreground': cssColor('--console-error', '#f87171'),
                'editorWarning.foreground': cssColor('--console-warning', '#fbbf24'),
                'scrollbarSlider.background': '#ffffff1f',
                'scrollbarSlider.hoverBackground': '#ffffff33'
            }
        });
    }

    function ensureMonaco() {
        if (!loading) {
            loading = (async () => {
                const css = document.createElement('link');
                css.rel = 'stylesheet';
                css.href = base + 'monaco.css';
                document.head.appendChild(css);
                self.MonacoEnvironment = {
                    getWorker: (_, label) => createWorker(label === 'json' ? 'json.worker.js' : 'editor.worker.js')
                };
                await loadScript(base + 'monaco.js');
                defineTheme();
                monaco.json.jsonDefaults.setDiagnosticsOptions({
                    validate: true,
                    allowComments: false,
                    enableSchemaRequest: false,
                    schemas: Object.entries(schemas).map(([id, s]) => ({ uri: s.uri, fileMatch: ['sherpa-' + id + '-*'], schema: s.schema }))
                });
                monaco.json.jsonDefaults.setModeConfiguration({ ...monaco.json.jsonDefaults.modeConfiguration, documentFormattingEdits: true });
            })();
            loading.catch(() => { loading = undefined; });
        }
        return loading;
    }

    let nextId = 0;

    window.monacoInterop = {
        async create(element, dotNetRef, options) {
            await ensureMonaco();
            if (!element.isConnected) return 0;
            const id = ++nextId;
            const uri = monaco.Uri.parse('inmemory://model/sherpa-' + (options.schema || 'plain') + '-' + id + '.json');
            const model = monaco.editor.createModel(options.value || '', 'json', uri);
            const editor = monaco.editor.create(element, {
                model,
                theme: 'sherpa-console',
                automaticLayout: true,
                minimap: { enabled: false },
                scrollBeyondLastLine: false,
                fontFamily: getComputedStyle(document.body).getPropertyValue('--font-mono').trim() || 'monospace',
                fontSize: 12,
                lineHeight: 18,
                tabSize: 2,
                insertSpaces: true,
                lineNumbersMinChars: 3,
                glyphMargin: false,
                folding: true,
                renderLineHighlight: 'line',
                overviewRulerLanes: 0,
                hideCursorInOverviewRuler: true,
                scrollbar: { verticalScrollbarSize: 10, horizontalScrollbarSize: 10, alwaysConsumeMouseWheel: false },
                padding: { top: 10, bottom: 10 },
                placeholder: options.placeholder || undefined,
                fixedOverflowWidgets: true,
                quickSuggestions: { strings: true, other: true, comments: false },
                suggestOnTriggerCharacters: true,
                formatOnPaste: false,
                ariaLabel: options.ariaLabel || 'JSON editor',
                contextmenu: true
            });
            const entry = { editor, model, suppress: false };
            entry.listener = model.onDidChangeContent(() => {
                if (!entry.suppress) dotNetRef.invokeMethodAsync('OnEditorChanged', model.getValue());
            });
            editors.set(id, entry);
            return id;
        },
        setValue(id, value) {
            const entry = editors.get(id);
            if (!entry || entry.model.getValue() === value) return;
            entry.suppress = true;
            try {
                // pushEditOperations keeps the change on the undo stack.
                entry.editor.pushUndoStop();
                entry.model.pushEditOperations([], [{ range: entry.model.getFullModelRange(), text: value }], () => null);
                entry.editor.pushUndoStop();
            } finally {
                entry.suppress = false;
            }
        },
        async format(id) {
            const entry = editors.get(id);
            if (entry) await entry.editor.getAction('editor.action.formatDocument')?.run();
        },
        focus(id) {
            editors.get(id)?.editor.focus();
        },
        dispose(id) {
            const entry = editors.get(id);
            if (!entry) return;
            editors.delete(id);
            entry.listener.dispose();
            entry.editor.dispose();
            entry.model.dispose();
        }
    };
})();

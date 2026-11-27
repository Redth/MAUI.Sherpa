// Bundles the Monaco editor core with only the JSON language into a few static files so the
// Blazor WebView can load it with plain <script>/<link> tags. Run: npm ci && npm run build
import { build } from 'esbuild';
import { copyFileSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const out = join(here, '../../src/MauiSherpa/wwwroot/vendor/monaco');
const pkg = join(here, 'node_modules/monaco-editor');

// Derive the entry from Monaco's own editor.main.js so every editor contribution (suggest, hover,
// folding, find, ...) stays in sync with the package, then drop all languages except JSON.
const editorDir = join(pkg, 'esm/vs/editor');
const keep = line =>
  !/external\/monaco-lsp-client|export \{ index as lsp \}/.test(line) &&
  !/languages\/(definitions|features)\/(?!json\/)/.test(line) &&
  !/export \{ __src_languages_features_(?!json)/.test(line);
const entry = join(here, 'node_modules/.sherpa-monaco-entry.js');
writeFileSync(entry, readFileSync(join(editorDir, 'editor.main.js'), 'utf8')
  .split('\n')
  .filter(keep)
  .map(line => line.replace(/(['"])(\.\.?\/[^'"]+)\1/, (_, q, rel) => q + join(editorDir, rel) + q))
  .join('\n') + '\nimport * as api from ' + JSON.stringify(join(editorDir, 'editor.api.js')) + ';\n' +
    'globalThis.monaco = { ...api, json: __src_languages_features_json_register_ts };\n');

rmSync(out, { recursive: true, force: true });
mkdirSync(out, { recursive: true });

const common = { bundle: true, minify: true, legalComments: 'none', target: 'safari16', logLevel: 'info' };

await build({
  ...common,
  entryPoints: { monaco: entry },
  outdir: out,
  format: 'iife',
  loader: { '.ttf': 'file' },
  assetNames: '[name]',
});

await build({
  ...common,
  entryPoints: {
    'editor.worker': join(pkg, 'esm/vs/editor/editor.worker.js'),
    'json.worker': join(pkg, 'esm/vs/language/json/json.worker.js'),
  },
  outdir: out,
  format: 'iife',
});

copyFileSync(join(pkg, 'LICENSE'), join(out, 'LICENSE'));
copyFileSync(join(pkg, 'ThirdPartyNotices.txt'), join(out, 'ThirdPartyNotices.txt'));

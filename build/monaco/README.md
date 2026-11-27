# Monaco bundle

Builds the trimmed Monaco Editor in `src/MauiSherpa/wwwroot/vendor/monaco` (editor
contributions plus the JSON language only) used by the push payload editors.

```bash
cd build/monaco
npm ci
npm run build
```

Commit `package.json`, `package-lock.json`, and the regenerated vendor files together.

# Submitting MAUI Sherpa to the official Homebrew Cask

This directory holds the reference cask used to publish **MAUI Sherpa** to the
official [`Homebrew/homebrew-cask`](https://github.com/Homebrew/homebrew-cask)
repository, so users can install with:

```bash
brew install --cask maui-sherpa
```

The [`Redth/homebrew-tap`](https://github.com/Redth/homebrew-tap) tap is kept as
a fallback (and is the only channel that carries pre-release builds). It is
auto-updated on every stable release by the `update-homebrew` job in
`.github/workflows/build.yml`.

## Why this project qualifies

`brew audit --new --cask` enforces a notability bar and that the app passes
Gatekeeper. MAUI Sherpa meets both:

- **Notable**: well above the ~75-star / 30-fork / 30-watcher threshold.
- **Signed + notarized + stapled**: the macOS app is hardened-runtime signed,
  notarized with `notarytool`, and stapled by the `notarize-macos` job.
- **Minimum macOS**: the binary's load command requires macOS **Sonoma (14)**,
  which is why the cask declares `depends_on macos: :sonoma`. Keep this in sync
  if the app's deployment target changes.

## One-time submission steps

1. Fork `Homebrew/homebrew-cask` (or let `brew bump-cask-pr` do it for you).
2. Make sure `maui-sherpa.rb` in this folder matches the latest stable release
   (version + sha256). The sha256 is for `MAUI-Sherpa.macos.zip`:

   ```bash
   VERSION=0.11.2
   curl -fSL -o /tmp/MAUI-Sherpa.macos.zip \
     "https://github.com/Redth/MAUI.Sherpa/releases/download/v${VERSION}/MAUI-Sherpa.macos.zip"
   shasum -a 256 /tmp/MAUI-Sherpa.macos.zip
   ```

3. Validate locally before opening the PR:

   ```bash
   brew style packaging/homebrew-cask/maui-sherpa.rb
   # Drop the file into a scratch tap to run the strict "new cask" audit:
   brew tap-new redth/casktest --no-git
   mkdir -p "$(brew --repository redth/casktest)/Casks/m"
   cp packaging/homebrew-cask/maui-sherpa.rb \
      "$(brew --repository redth/casktest)/Casks/m/maui-sherpa.rb"
   brew audit --new --cask --online redth/casktest/maui-sherpa
   brew untap redth/casktest   # cleanup
   ```

4. Copy the cask into your homebrew-cask fork at `Casks/m/maui-sherpa.rb`,
   commit on a branch named `maui-sherpa`, and open a PR titled
   `Add maui-sherpa <version>`.

## After it's accepted

- Future version bumps are handled automatically by Homebrew's BrewTestBot
  (driven by the `livecheck` stanza) — no manual PRs needed.
- Keep the `update-homebrew` job so the `Redth/homebrew-tap` fallback and
  pre-release channel stay current.
- The README points users at `brew install --cask maui-sherpa` with the tap as
  a documented fallback.

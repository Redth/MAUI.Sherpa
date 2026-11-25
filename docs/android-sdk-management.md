# Android SDK selection

Sherpa uses one active Android SDK for packages, device tools, and emulators.
The SDK Packages and Android Devices pages show its full location in a compact
context line. Their Android SDK toolbar menu provides switching, installation,
copy-path, and reveal-in-file-manager actions. Platforms without a native toolbar
show these actions in the page content instead.

## Use an existing SDK

Choose **Switch SDK...**, select a discovered location or browse to a custom
folder, then choose **Use This SDK**. The folder must contain Android command-line
tools (`sdkmanager`). Sherpa remembers the selection across restarts. Settings
also offers **Reset to Default** to return to automatic discovery.

Discovery includes Android Studio/MAUI defaults, legacy Xamarin/Visual Studio
locations, `ANDROID_HOME`, `ANDROID_SDK_ROOT`, and previously used custom folders.
Shared locations are deduplicated. A saved SDK that was moved or deleted produces
an explicit error; use Switch SDK to select its replacement.

## Install another SDK

**Install SDK...** is available even when an SDK is already selected. Choose an
empty dedicated folder or an incomplete SDK installation. Install New shows only
the destination controls, with common locations in a picker. Folders containing
unrelated files are rejected. An existing SDK destination shows a warning and
disables installation; select it from Use Existing instead.

Installation downloads Android command-line tools. Add Android platforms, build
tools, platform-tools, and emulator images from SDK Packages afterward. Configure
OpenJDK in Settings for tools that require Java.

**Use this SDK after installation** is selected by default. Uncheck it to install
another SDK without changing the active one. Failed installations do not replace
the active SDK. Installation is not cancellable; partial SDK folders can be retried.

## Switching behavior

SDK operations are serialized, and switching is unavailable during SDK changes.
Switching refreshes SDK-dependent caches and reconnects the device watcher.
Inspector tabs reconnect using the new SDK. Running emulators and shared AVD
folders are not deleted or migrated. An emulator whose image is absent from the
active SDK remains visible with a warning; install that image before starting it.

Selection changes Sherpa only. It does not edit environment variables, Android
Studio settings, project configuration, or the shared AVD location.

## Configure terminal PATH

Choose **Android SDK > Check / Configure PATH...** to inspect `adb`, `fastboot`,
`emulator`, `sdkmanager`, and `avdmanager`. Optionally include the newest stable
installed build tools (`aapt`, `aapt2`, `apksigner`, and `zipalign`). Missing tools
must first be installed from SDK Packages.

The dialog shows which executable each PATH currently finds, the selected
configuration destination, and the exact proposed update. Shell inspection runs
an installed shell with its normal startup files. Changes happen only after
**Apply PATH Update** and confirmation; opening the dialog changes no PATH files.

User-only updates are the default. **All users** is offered only when the SDK
and its tools are outside user-local locations, including other users' home
folders and symlinks into them. Machine-wide updates require native administrator
approval (polkit/`pkexec` on Linux). Windows searches Machine PATH before User PATH,
so a user-only change cannot override a conflicting machine-wide SDK entry.

Windows updates the chosen environment registry PATH while preserving unrelated
entries and expandable variables. macOS/Linux support zsh, bash, POSIX sh, and
fish. Choose **Bash - interactive** for `.bashrc`, or **Bash - login** for the
first existing `.bash_profile`, `.bash_login`, or `.profile`. Zsh uses `.zshrc`
(honoring `ZDOTDIR`); fish uses a dedicated `conf.d` file under `XDG_CONFIG_HOME`.
Global shell updates use the corresponding system startup file. Startup files
that reset PATH afterward can override these changes; use Check Again to inspect.

Sherpa replaces only its marked block in shell files, keeps existing file
permissions, and saves adjacent `.sherpa-backup-*` copies before replacing existing
files. Windows backups are saved as `windows-*-path-backup-*.txt` in app data.
Changed previews must be refreshed before applying. Verification checks the
saved configuration; open a new terminal for its environment to take effect.
Running terminals/apps keep their old environment. Switching SDKs does not
automatically rewrite PATH; explicitly apply again to select a different SDK
for external tools.

# Android SDK & JDK management

Sherpa uses one active Android SDK for packages, device tools, and emulators, and
one active JDK to run the Android tools. Both are managed from **SDK & JDK** in the sidebar (they are no longer configured in Settings).

The top of the page summarizes the active OpenJDK version and the Android SDK
command-line tools version, with each location. **Manage...** opens a dialog that
lists installed copies, installs more, changes the active one, and removes old
ones. An available JDK update appears as
a button showing the new version. Each row shows its version as a chip and its
path with a copy button. Android packages appear below once both an SDK and a JDK
are available.

When either is missing, the page shows a setup view instead of the package list,
with an **Install...** button for each missing tool that opens its dialog directly
in the install flow with the recommended defaults. The Android Devices page also
shows the SDK location and its toolbar menu (switch, install, copy path, reveal).

## Use an existing SDK

Choose **Manage...** (or **Switch SDK...** from the toolbar menu), select a
discovered location on the **Installed** tab or browse to a custom folder, then
choose **Use This SDK**. The folder must contain Android command-line tools
(`sdkmanager`). Sherpa remembers the selection across restarts.

Inactive installed SDKs have a delete button. Deletion asks for confirmation and
permanently removes that SDK folder; the active SDK cannot be deleted.

Discovery includes Android Studio/MAUI defaults, legacy Xamarin/Visual Studio
locations, `ANDROID_HOME`, `ANDROID_SDK_ROOT`, and previously used custom folders.
Shared locations are deduplicated. A saved SDK that was moved or deleted produces
an explicit error; use Switch SDK to select its replacement.

## Install another SDK

**Install SDK...** is available even when an SDK is already selected. Choose an
empty dedicated folder or an incomplete SDK installation. Install New shows only
the destination controls, with common locations in a picker. Folders containing
unrelated files are rejected. An existing SDK destination shows a warning and
disables installation; select it from Installed instead.

Installation downloads Android command-line tools. Add Android platforms, build
tools, platform-tools, and emulator images from the package list afterward. OpenJDK
is required: the installer runs Java after downloading the tools. If Java
is missing, Sherpa opens OpenJDK setup before starting SDK installation. Cancelling
JDK setup leaves the SDK untouched.

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

## Microsoft OpenJDK

The OpenJDK row offers **Install OpenJDK...** or **Manage...**. The native hybrid
dialog lets you select an existing JDK or install Microsoft's
latest published build for a Java major. **21 is recommended for Android**;
other majors **17 and newer** from Microsoft's download catalog are available
for projects with different requirements. A newer Java major is not necessarily compatible with
older Android tools.

Sherpa resolves the current release from Microsoft's official download page for
the computer's OS and architecture, and refreshes it again when installation
starts. Downloads are SHA-256 verified, extracted, and checked by running both
`java -version` and `javac -version` before publishing the installation. Failed or
cancelled downloads are not activated.

Installs live side by side under `jdks/microsoft-jdk-<version>` in Sherpa's
application data folder and need no administrator access. Installation selects
the new JDK for Sherpa; it does not remove older builds. Inactive Sherpa-installed
JDKs can be removed from the **Installed** tab after confirmation; JDKs installed
by other tools are never deleted. Existing JDKs remain
selectable, including builds installed outside Sherpa. On macOS, select the
`Contents/Home` folder inside a `.jdk` bundle.

The selected home is persisted. Sherpa sets **only its own process's** `JAVA_HOME`
and `JDK_HOME`, and rebuilds Android tool instances after a JDK switch. It never
edits shell configuration, machine/user environment settings, Android Studio, or
project settings.

While the app is running, Microsoft installations are checked for updates
after startup and every six hours, with the release catalog cached for 24 hours.
The page displays available updates; **Refresh** in the JDK
dialog forces a fresh check. Update offers stay within the installed major and
require explicit installation. For external Microsoft installations, patch
updates are offered as a new Sherpa-managed installation; external files are
never replaced. Offline failures are logged and shown with a retry instruction.

## Configure terminal PATH

When the terminal finds Android tools outside the selected SDK, or doesn't find
them at all, the Android SDK row shows a **Fix PATH...** button that opens the
same dialog described below. PATH checks cover Android SDK tools only, not the JDK.

Choose **Android SDK > Check / Configure PATH...** to inspect `adb`, `fastboot`,
`emulator`, `sdkmanager`, and `avdmanager`. Optionally include the newest stable
installed build tools (`aapt`, `aapt2`, `apksigner`, and `zipalign`). Missing tools
must first be installed from the package list.

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

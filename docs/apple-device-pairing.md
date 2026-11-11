# Apple device pairing

Open **Apple > Devices & Simulators** and choose **Pair Device** in the toolbar to pair a physical
device with this Mac without opening Xcode.
This is separate from registering a device in an Apple Developer account.

The pairing screen is a native hybrid sheet: native title and action buttons surround a themed
Blazor body laid out like macOS Bluetooth settings. **Available Devices** comes first and lists
unpaired devices Sherpa can see; it refreshes every few seconds, so a newly connected cable shows up
on its own. Its header holds **Pair over Wi-Fi…**. **My Devices** lists devices already paired with
this Mac, with their live connection status. **Stop** cancels the current operation without revoking
trust; **Done** dismisses the sheet.

## USB

Connect and unlock the device. When it appears under **Available Devices**, choose **Pair** on its
row. Sherpa targets the device's stable CoreDevice identifier and runs Apple's documented
`xcrun devicectl manage pair` command. Complete the device's Trust/passcode prompt and any Mac
authorization prompt yourself. Sherpa never supplies authentication answers.

Paired devices offer **Check** instead. This does not re-pair the device. A successful
live check queries `devicectl device info lockState`; cached device details alone are not considered
proof that the device is responding.

## Unpairing

The **Physical Devices** section also has an **Unpair device** action on each paired device card,
including offline devices. A confirmation explains that unpairing removes this Mac's trust
relationship, not the device's data. Connected cards show **USB** or **Wi-Fi**; offline cards do not
present the last-known transport as a current connection. The pairing sheet uses the same labels.

Each row under **My Devices** has an unpair button. Sherpa asks for confirmation inside that row
before doing anything. After you choose **Unpair**, it re-reads the device by stable identifier, runs
Apple's documented `xcrun devicectl manage unpair` command, and only reports success once Apple no
longer lists the device as paired. Pairing again requires Trust and the device passcode. Sherpa never
unpairs a device on its own, during pairing, or to make another flow work.

Xcode must be installed and selected with `xcode-select`. Apple devices supported by CoreDevice are
listed, excluding simulators and the Mac host. USB pairing has been verified with an iPad on iPadOS
17.7.9. Other Apple device families are not yet hardware-verified.

## Experimental Wi-Fi

**This uses a private Apple API, not a documented first-time iPhone pairing command.** The protocol
was verified with RemotePairing 280.40.3 on macOS 27.2 and a fresh iPhone on iOS 27.2. Apple can change
or remove it. Other OS versions, first-time iPad Wi-Fi pairing, tvOS, visionOS, and watchOS have not
been verified. Use USB when the private helper is unavailable or incompatible.

On iOS/iPadOS 27 or later, enable Developer Mode and put the device on the same local network as the
Mac. In Sherpa, choose **Wi-Fi (Experimental)**, acknowledge the warning, and explicitly start.
On the device, open **Settings > Developer > Paired Macs > Other Devices**, select this Mac, and
enter the temporary code shown by Sherpa. Do not remove an existing pairing to make the Mac appear.

The bundled `sherpa-apple-pairing` helper loads Apple's RemotePairing framework and requests a
device-initiated host beacon through its bundled XPC service. It uses Apple's native pairing store;
there is no third-party credential store or fallback. Pairing codes travel only through a redirected
pipe into the dialog; they are not logged, copied to a terminal, or written to result files.
The `sendingDevice` payload is not used as a trusted device name or identifier.

The session lasts at most three minutes. Stop, close, navigation, parent-process termination,
timeout, failure, and success end the helper's owned connections and withdraw its advertisement.
Codes are removed from the dialog when the operation stops or completes. .NET strings are managed
memory; clearing the UI is not a guarantee of immediate cryptographic memory erasure.

After Apple's success event, Sherpa compares stable pairing identifiers with a preflight snapshot
and checks a uniquely newly-paired device. If no unique device can be identified, it explicitly
reports that Apple's exchange completed but device verification is still needed. It never uses an
already-paired, same-name phone as proof of a new pairing.

## Failure and retry

Cancellation and failed commands can leave a pairing that the owner already approved. Sherpa
refreshes device state after an attempt; refresh errors are shown separately from the pairing result.
Unlock the device and inspect its state before explicitly retrying. Sherpa never revokes trust,
disables Developer Mode, automatically retries a pairing operation, or unpairs without your
confirmation.

Native helper distribution/notarization and other OS combinations still require validation. The
helper is built for arm64 and x86_64, bundled in `Contents/MacOS`, and signed before the outer app.
The release workflow re-signs it with a Developer ID, hardened runtime, and a secure timestamp,
then verifies each architecture before notarization. No new entitlement or sandbox bypass is used.

The fresh USB and Wi-Fi pairings above were verified with the standalone prototype. The integrated
Sherpa build has been checked for discovery, an existing iPad's live connection, helper availability,
and consecutive signed macOS builds without starting another real pairing. The grouped native sheet
has been checked for layout, native footer actions, and Wi-Fi acknowledgment gating. Unpairing is
covered by automated tests only; it has not been run against real hardware. Automated tests cover the
web-to-native Tab handoff; complete native keyboard traversal still needs interactive confirmation.
Fresh pairings through the packaged dialog still need consenting devices and owner-completed
authentication.

## Simulators

Simulators are listed separately from physical devices, including unavailable simulators whose
runtime is no longer installed. Use **Delete simulator** (the trash button on its card) to remove a
simulator and all of its apps and data. Sherpa asks for confirmation, shuts down a running simulator
first, and stops without deleting if shutdown fails. Deletion cannot be undone. This deletes the
simulator, not its shared runtime.

To delete several at once, choose **Select** beside the Simulators heading, check individual
simulators or use **Select all shown**, then choose **Delete Selected**. Select all applies only to
the current search and filters; the selection count calls out selected simulators hidden by filters.
Review the batch list and choose **Run … Operations** to confirm. Each simulator is stopped if running and
deleted in turn. A failure does not stop the rest of the batch. Failed or skipped simulators stay
selected for another attempt; successfully deleted simulators are removed from the selection.
**Cancel Selection** leaves all simulators unchanged.

## Development checks

```sh
dotnet test tests/MauiSherpa.Core.Tests --filter 'FullyQualifiedName~AppleDevicePairingServiceTests|FullyQualifiedName~SimulatorDeletionTests'
node --test tests/javascript/modalInterop.test.cjs
dotnet build src/MauiSherpa.MacOS -f net10.0-macos

HELPER="src/MauiSherpa.MacOS/bin/Debug/net10.0-macos/osx-arm64/MAUI Sherpa.app/Contents/MacOS/sherpa-apple-pairing"
"$HELPER" --self-test
APPLE_PAIRING_HELPER="$HELPER" python3 -B tests/native/test_apple_pairing_host.py
"$HELPER" --probe
```

The native tests exercise decoding, parent-pipe EOF, SIGINT, SIGTERM, deadline cleanup, and refusal
of unconfirmed/unbounded advertising **without loading RemotePairing or advertising the Mac**.
`--probe` is a read-only service check. Do not start real advertising or another pairing just to run
automated tests; use consenting hardware and let the owner handle all authentication.

Apple documentation:

- [Device Hub](https://developer.apple.com/documentation/xcode/managing-your-simulated-and-physical-devices-in-device-hub)
- [Command-line device tools](https://developer.apple.com/documentation/xcode/interacting-with-devices-using-the-command-line)
- [Developer Mode](https://developer.apple.com/documentation/xcode/enabling-developer-mode-on-a-device)

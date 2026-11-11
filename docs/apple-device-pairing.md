# Apple device pairing

Open **Apple > Devices & Simulators > Pair Device** to pair a physical device with this Mac without opening Xcode.
This is separate from registering a device in an Apple Developer account.

## USB

Connect and unlock the device, refresh the list, select its stable CoreDevice identifier, and choose
**Pair via USB**. Sherpa runs Apple's documented `xcrun devicectl manage pair` command. Complete the
device's Trust/passcode prompt and any Mac authorization prompt yourself. Sherpa never supplies
authentication answers.

An already-paired device offers **Check Connection** instead. This does not re-pair the device.
Pairing records, current connection, transport, and Developer Mode are shown separately. A successful
live check queries `devicectl device info lockState`; cached device details alone are not considered
proof that the device is responding.

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
Unlock the device and inspect its state before explicitly retrying. Sherpa never unpairs, revokes
trust, disables Developer Mode, or automatically retries a pairing operation.

Native helper distribution/notarization and other OS combinations still require validation. The
helper is built for arm64 and x86_64, bundled in `Contents/MacOS`, and signed before the outer app.
No new entitlement or sandbox bypass is used.

The fresh USB and Wi-Fi pairings above were verified with the standalone prototype. The integrated
Sherpa build has been checked for discovery, an existing iPad's live connection, helper availability,
both themes, dialog keyboard navigation, and consecutive signed macOS builds without starting another
real pairing. A fresh pairing through the packaged dialog
still needs a consenting device and owner-completed authentication.

## Development checks

```sh
dotnet test tests/MauiSherpa.Core.Tests --filter FullyQualifiedName~AppleDevicePairingServiceTests
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

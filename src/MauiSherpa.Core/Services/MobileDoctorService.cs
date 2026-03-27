using System.Diagnostics;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public class MobileDoctorService : IMobileDoctorService
{
    private readonly IAndroidSdkSettingsService _sdkSettings;
    private readonly IOpenJdkSettingsService _jdkSettings;
    private readonly IXcodeService _xcodeService;
    private readonly ISimulatorService _simulatorService;
    private readonly IPlatformService _platform;

    public MobileDoctorService(
        IAndroidSdkSettingsService sdkSettings,
        IOpenJdkSettingsService jdkSettings,
        IXcodeService xcodeService,
        ISimulatorService simulatorService,
        IPlatformService platform)
    {
        _sdkSettings = sdkSettings;
        _jdkSettings = jdkSettings;
        _xcodeService = xcodeService;
        _simulatorService = simulatorService;
        _platform = platform;
    }

    public async Task<MobileDoctorReport> RunDoctorAsync(IProgress<string>? progress = null)
    {
        var sections = new List<MobileDoctorSection>();

        progress?.Report("Checking Android toolchain...");
        sections.Add(await BuildAndroidSectionAsync());

        progress?.Report("Checking Java toolchain...");
        sections.Add(await BuildJdkSectionAsync());

        if (_platform.IsMacOS || _platform.IsMacCatalyst)
        {
            progress?.Report("Checking Apple toolchain...");
            sections.Add(await BuildAppleSectionAsync());
        }

        progress?.Report("Mobile doctor complete");
        return new MobileDoctorReport(sections, DateTime.UtcNow);
    }

    private async Task<MobileDoctorSection> BuildAndroidSectionAsync()
    {
        var checks = new List<MobileDoctorCheck>();
        var sdkPath = await _sdkSettings.GetEffectiveSdkPathAsync();

        if (string.IsNullOrWhiteSpace(sdkPath) || !Directory.Exists(sdkPath))
        {
            checks.Add(new MobileDoctorCheck(
                "Android SDK",
                MobileDoctorCheckStatus.Error,
                "Android SDK not found",
                ["Set the SDK path in Settings or install the Android SDK."]));

            return new MobileDoctorSection("Android", checks);
        }

        checks.Add(new MobileDoctorCheck(
            "Android SDK",
            MobileDoctorCheckStatus.Ok,
            sdkPath,
            [$"Path: {sdkPath}"]));

        var adbPath = AppDataPath.GetAdbPath(sdkPath);
        checks.Add(new MobileDoctorCheck(
            "Platform Tools",
            File.Exists(adbPath) ? MobileDoctorCheckStatus.Ok : MobileDoctorCheckStatus.Error,
            File.Exists(adbPath) ? "adb is available" : "adb not found",
            [$"Expected at: {adbPath}"]));

        var emulatorDir = Path.Combine(sdkPath, "emulator");
        checks.Add(new MobileDoctorCheck(
            "Android Emulator",
            Directory.Exists(emulatorDir) ? MobileDoctorCheckStatus.Ok : MobileDoctorCheckStatus.Warning,
            Directory.Exists(emulatorDir) ? "Emulator tools are installed" : "Emulator tools are not installed",
            [$"Expected at: {emulatorDir}"]));

        return new MobileDoctorSection("Android", checks);
    }

    private async Task<MobileDoctorSection> BuildJdkSectionAsync()
    {
        var checks = new List<MobileDoctorCheck>();
        var jdkPath = await _jdkSettings.GetEffectiveJdkPathAsync();

        if (string.IsNullOrWhiteSpace(jdkPath) || !Directory.Exists(jdkPath))
        {
            checks.Add(new MobileDoctorCheck(
                "OpenJDK",
                MobileDoctorCheckStatus.Error,
                "JDK not found",
                ["Install OpenJDK or set a custom JDK path in Settings."]));

            return new MobileDoctorSection("Java", checks);
        }

        var javaBinary = Path.Combine(jdkPath, "bin", OperatingSystem.IsWindows() ? "java.exe" : "java");
        var keytoolBinary = Path.Combine(jdkPath, "bin", OperatingSystem.IsWindows() ? "keytool.exe" : "keytool");

        checks.Add(new MobileDoctorCheck(
            "OpenJDK",
            File.Exists(javaBinary) ? MobileDoctorCheckStatus.Ok : MobileDoctorCheckStatus.Warning,
            File.Exists(javaBinary) ? "java is available" : "java binary not found",
            [$"Path: {jdkPath}"]));

        checks.Add(new MobileDoctorCheck(
            "Keytool",
            File.Exists(keytoolBinary) ? MobileDoctorCheckStatus.Ok : MobileDoctorCheckStatus.Warning,
            File.Exists(keytoolBinary) ? "keytool is available" : "keytool binary not found",
            [$"Expected at: {keytoolBinary}"]));

        var version = await TryReadJavaVersionAsync(javaBinary);
        if (!string.IsNullOrWhiteSpace(version))
        {
            checks.Add(new MobileDoctorCheck(
                "Java Version",
                MobileDoctorCheckStatus.Ok,
                version));
        }

        return new MobileDoctorSection("Java", checks);
    }

    private async Task<MobileDoctorSection> BuildAppleSectionAsync()
    {
        var checks = new List<MobileDoctorCheck>();

        if (!_xcodeService.IsSupported)
        {
            checks.Add(new MobileDoctorCheck(
                "Xcode",
                MobileDoctorCheckStatus.Warning,
                "Apple tooling is not supported on this platform"));

            return new MobileDoctorSection("Apple", checks);
        }

        var selectedPath = await _xcodeService.GetSelectedXcodePathAsync();
        checks.Add(new MobileDoctorCheck(
            "Selected Xcode",
            string.IsNullOrWhiteSpace(selectedPath) ? MobileDoctorCheckStatus.Error : MobileDoctorCheckStatus.Ok,
            string.IsNullOrWhiteSpace(selectedPath) ? "xcode-select is not configured" : selectedPath!,
            string.IsNullOrWhiteSpace(selectedPath)
                ? ["Run xcode-select -p or choose an Xcode installation from Xcode Management."]
                : [$"Path: {selectedPath}"]));

        var installedXcodes = await _xcodeService.GetInstalledXcodesAsync();
        checks.Add(new MobileDoctorCheck(
            "Installed Xcodes",
            installedXcodes.Count > 0 ? MobileDoctorCheckStatus.Ok : MobileDoctorCheckStatus.Warning,
            installedXcodes.Count > 0 ? $"{installedXcodes.Count} Xcode installation(s) found" : "No Xcode installations found"));

        if (_simulatorService.IsSupported)
        {
            try
            {
                var simulators = await _simulatorService.GetSimulatorsAsync();
                var runtimes = await _simulatorService.GetRuntimesAsync();

                checks.Add(new MobileDoctorCheck(
                    "Simulators",
                    simulators.Count > 0 ? MobileDoctorCheckStatus.Ok : MobileDoctorCheckStatus.Warning,
                    simulators.Count > 0 ? $"{simulators.Count} simulator device(s) available" : "No simulator devices available"));

                checks.Add(new MobileDoctorCheck(
                    "Simulator Runtimes",
                    runtimes.Count > 0 ? MobileDoctorCheckStatus.Ok : MobileDoctorCheckStatus.Warning,
                    runtimes.Count > 0 ? $"{runtimes.Count} runtime(s) installed" : "No simulator runtimes found"));
            }
            catch (Exception ex)
            {
                checks.Add(new MobileDoctorCheck(
                    "Simulators",
                    MobileDoctorCheckStatus.Warning,
                    $"Could not query simulators: {ex.Message}"));
            }
        }

        return new MobileDoctorSection("Apple", checks);
    }

    private static async Task<string?> TryReadJavaVersionAsync(string javaBinary)
    {
        if (!File.Exists(javaBinary))
        {
            return null;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = javaBinary,
                Arguments = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start {javaBinary}");
            var output = await process.StandardError.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(output))
            {
                output = await process.StandardOutput.ReadToEndAsync();
            }

            await process.WaitForExitAsync();
            return output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()?
                .Trim();
        }
        catch
        {
            return null;
        }
    }
}

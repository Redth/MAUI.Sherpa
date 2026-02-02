using AndroidSdk;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public class AndroidSdkService : IAndroidSdkService
{
    private AndroidSdkManager? _sdkManager;
    private readonly ILoggingService _logger;

    public string? SdkPath => _sdkManager?.Home?.FullName;
    public bool IsSdkInstalled => _sdkManager?.Home != null && Directory.Exists(_sdkManager.Home.FullName);
    
    public event Action? SdkPathChanged;

    public AndroidSdkService(ILoggingService logger)
    {
        _logger = logger;
    }

    public Task<bool> DetectSdkAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                _sdkManager = new AndroidSdkManager();
                if (_sdkManager.Home != null)
                {
                    _logger.LogInformation($"Android SDK detected at: {_sdkManager.Home.FullName}");
                    SdkPathChanged?.Invoke();
                    return true;
                }
                _logger.LogWarning("Android SDK not found");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Android SDK not found: {ex.Message}");
                _sdkManager = null;
                return false;
            }
        });
    }

    public Task<bool> SetSdkPathAsync(string path)
    {
        return Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    _logger.LogError($"SDK path does not exist: {path}");
                    return false;
                }

                _sdkManager = new AndroidSdkManager(new DirectoryInfo(path));
                
                if (_sdkManager.Home != null)
                {
                    _logger.LogInformation($"Android SDK set to: {_sdkManager.Home.FullName}");
                    SdkPathChanged?.Invoke();
                    return true;
                }
                
                _logger.LogWarning($"Could not initialize SDK at: {path}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to set SDK path: {ex.Message}", ex);
                _sdkManager = null;
                return false;
            }
        });
    }

    public Task<string?> GetDefaultSdkPathAsync()
    {
        return Task.Run<string?>(() =>
        {
            try
            {
                // Create a temporary manager to detect the default path
                var tempManager = new AndroidSdkManager();
                return tempManager.Home?.FullName;
            }
            catch
            {
                return null;
            }
        });
    }

    public Task<IReadOnlyList<SdkPackageInfo>> GetInstalledPackagesAsync()
    {
        return Task.Run<IReadOnlyList<SdkPackageInfo>>(() =>
        {
            if (_sdkManager == null)
                return Array.Empty<SdkPackageInfo>();

            try
            {
                var list = _sdkManager.SdkManager.List();
                var packages = list.InstalledPackages
                    .Select(p => new SdkPackageInfo(
                        Path: p.Path,
                        Description: p.Description,
                        Version: p.Version,
                        Location: p.Location,
                        IsInstalled: true))
                    .ToList();
                
                _logger.LogDebug($"GetInstalledPackagesAsync: Found {packages.Count} packages");
                foreach (var pkg in packages.Take(5))
                {
                    _logger.LogDebug($"  Installed: {pkg.Path} v{pkg.Version}");
                }
                
                return packages;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to list installed packages: {ex.Message}", ex);
                return Array.Empty<SdkPackageInfo>();
            }
        });
    }

    public Task<IReadOnlyList<SdkPackageInfo>> GetAvailablePackagesAsync()
    {
        return Task.Run<IReadOnlyList<SdkPackageInfo>>(() =>
        {
            if (_sdkManager == null)
                return Array.Empty<SdkPackageInfo>();

            try
            {
                var list = _sdkManager.SdkManager.List();
                var packages = list.AvailablePackages
                    .Select(p => new SdkPackageInfo(
                        Path: p.Path,
                        Description: p.Description,
                        Version: p.Version,
                        Location: null,
                        IsInstalled: false))
                    .ToList();
                
                _logger.LogDebug($"GetAvailablePackagesAsync: Found {packages.Count} packages");
                foreach (var pkg in packages.Where(p => p.Path == "emulator" || p.Path?.Contains("build-tools") == true).Take(5))
                {
                    _logger.LogDebug($"  Available: {pkg.Path} v{pkg.Version}");
                }
                
                return packages;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to list available packages: {ex.Message}", ex);
                return Array.Empty<SdkPackageInfo>();
            }
        });
    }

    public Task<bool> InstallPackageAsync(string packagePath, IProgress<string>? progress = null)
    {
        return Task.Run(() =>
        {
            if (_sdkManager == null)
            {
                _logger.LogError("SDK not initialized");
                progress?.Report("Error: SDK not initialized");
                return false;
            }

            try
            {
                _logger.LogInformation($"Starting install of package: {packagePath}");
                _logger.LogDebug($"SDK Home: {_sdkManager.Home?.FullName}");
                
                progress?.Report($"Installing {packagePath}...");
                
                _logger.LogDebug($"Calling sdkmanager install for: {packagePath}");
                _sdkManager.SdkManager.Install(packagePath);
                _logger.LogDebug("Install call completed without exception");
                
                // Verify the install by checking if package now appears in installed list
                _logger.LogDebug("Verifying installation...");
                progress?.Report("Verifying installation...");
                
                var installedList = _sdkManager.SdkManager.List();
                var installedPkgs = installedList.InstalledPackages ?? Enumerable.Empty<AndroidSdk.SdkManager.InstalledSdkPackage>();
                var installedCount = installedPkgs.Count();
                _logger.LogDebug($"Found {installedCount} installed packages after install");
                
                var isNowInstalled = installedPkgs.Any(p => 
                    p.Path?.Equals(packagePath, StringComparison.OrdinalIgnoreCase) == true);
                
                if (isNowInstalled)
                {
                    var installedPkg = installedPkgs.First(p => p.Path?.Equals(packagePath, StringComparison.OrdinalIgnoreCase) == true);
                    progress?.Report($"Successfully installed {packagePath} (version: {installedPkg.Version})");
                    _logger.LogInformation($"Verified: Package {packagePath} is now installed (version: {installedPkg.Version})");
                    return true;
                }
                else
                {
                    _logger.LogWarning($"Package {packagePath} install completed but package not found in installed list");
                    _logger.LogWarning($"Installed packages: {string.Join(", ", installedPkgs.Take(10).Select(p => p.Path))}...");
                    progress?.Report($"Warning: Install completed but verification failed for {packagePath}");
                    // Return false since we can't verify the install worked
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to install package {packagePath}: {ex.Message}", ex);
                _logger.LogError($"Exception type: {ex.GetType().Name}");
                if (ex.InnerException != null)
                {
                    _logger.LogError($"Inner exception: {ex.InnerException.Message}");
                }
                progress?.Report($"Error: {ex.Message}");
                return false;
            }
        });
    }

    public Task<bool> UninstallPackageAsync(string packagePath)
    {
        return Task.Run(() =>
        {
            if (_sdkManager == null)
            {
                _logger.LogError("SDK not initialized");
                return false;
            }

            try
            {
                _sdkManager.SdkManager.Uninstall(packagePath);
                _logger.LogInformation($"Uninstalled package: {packagePath}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to uninstall package {packagePath}: {ex.Message}", ex);
                return false;
            }
        });
    }

    public Task<IReadOnlyList<DeviceInfo>> GetDevicesAsync()
    {
        return Task.Run<IReadOnlyList<DeviceInfo>>(() =>
        {
            if (_sdkManager == null)
                return Array.Empty<DeviceInfo>();

            try
            {
                var devices = _sdkManager.Adb.GetDevices();
                return devices
                    .Select(d => new DeviceInfo(
                        Serial: d.Serial,
                        State: _sdkManager.Adb.GetState(d.Serial) ?? "unknown",
                        Model: d.Model,
                        IsEmulator: d.Serial?.StartsWith("emulator") ?? false))
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to get devices: {ex.Message}", ex);
                return Array.Empty<DeviceInfo>();
            }
        });
    }

    public Task<bool> AcquireSdkAsync(string? targetPath = null, IProgress<string>? progress = null)
    {
        return Task.Run(async () =>
        {
            try
            {
                progress?.Report("Downloading Android SDK...");
                
                if (string.IsNullOrEmpty(targetPath))
                {
                    targetPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "android-sdk");
                }

                _sdkManager = new AndroidSdkManager(new DirectoryInfo(targetPath));
                await _sdkManager.Acquire();
                
                progress?.Report($"Android SDK installed at: {targetPath}");
                _logger.LogInformation($"Acquired Android SDK at: {targetPath}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to acquire SDK: {ex.Message}", ex);
                progress?.Report($"Failed to acquire SDK: {ex.Message}");
                return false;
            }
        });
    }

    public Task<IReadOnlyList<Interfaces.AvdInfo>> GetAvdsAsync()
    {
        return Task.Run<IReadOnlyList<Interfaces.AvdInfo>>(() =>
        {
            if (_sdkManager == null)
                return Array.Empty<Interfaces.AvdInfo>();

            try
            {
                var avds = _sdkManager.AvdManager.ListAvds();
                return avds
                    .Select(a => new Interfaces.AvdInfo(
                        Name: a.Name,
                        Device: a.Device,
                        Path: a.Path,
                        Target: a.Target,
                        BasedOn: a.BasedOn,
                        Properties: new Dictionary<string, string>(a.Properties)))
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to list AVDs: {ex.Message}", ex);
                return Array.Empty<Interfaces.AvdInfo>();
            }
        });
    }

    public Task<IReadOnlyList<AvdDeviceDefinition>> GetAvdDeviceDefinitionsAsync()
    {
        return Task.Run<IReadOnlyList<AvdDeviceDefinition>>(() =>
        {
            if (_sdkManager == null)
                return Array.Empty<AvdDeviceDefinition>();

            try
            {
                var devices = _sdkManager.AvdManager.ListDevices();
                return devices
                    .Select(d => new AvdDeviceDefinition(
                        Id: d.Id,
                        Name: d.Name,
                        Oem: d.Oem,
                        NumericId: d.NumericId))
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to list device definitions: {ex.Message}", ex);
                return Array.Empty<AvdDeviceDefinition>();
            }
        });
    }

    public Task<IReadOnlyList<string>> GetSystemImagesAsync()
    {
        return Task.Run<IReadOnlyList<string>>(() =>
        {
            if (_sdkManager == null)
                return Array.Empty<string>();

            try
            {
                var list = _sdkManager.SdkManager.List();
                return list.InstalledPackages
                    .Where(p => p.Path.StartsWith("system-images;"))
                    .Select(p => p.Path)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to list system images: {ex.Message}", ex);
                return Array.Empty<string>();
            }
        });
    }

    public Task<IReadOnlyList<string>> GetAvdSkinsAsync()
    {
        return Task.Run<IReadOnlyList<string>>(() =>
        {
            if (_sdkManager?.Home == null)
                return Array.Empty<string>();

            try
            {
                var skinsDir = System.IO.Path.Combine(_sdkManager.Home.FullName, "skins");
                if (Directory.Exists(skinsDir))
                {
                    return Directory.GetDirectories(skinsDir)
                        .Select(System.IO.Path.GetFileName)
                        .Where(s => s != null)
                        .Cast<string>()
                        .OrderBy(s => s)
                        .ToList();
                }
                return Array.Empty<string>();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to list AVD skins: {ex.Message}", ex);
                return Array.Empty<string>();
            }
        });
    }

    public Task<bool> CreateAvdAsync(string name, string systemImage, EmulatorCreateOptions? options = null, IProgress<string>? progress = null)
    {
        return Task.Run(() =>
        {
            if (_sdkManager == null)
            {
                _logger.LogError("SDK not initialized");
                return false;
            }

            try
            {
                progress?.Report($"Creating emulator '{name}'...");
                
                _sdkManager.AvdManager.Create(
                    name, 
                    systemImage, 
                    device: options?.Device,
                    path: options?.CustomPath,
                    force: true,
                    sdCardSize: options?.SdCardSize);
                
                // Apply additional config.ini settings if specified
                if (options?.RamSizeMb != null || options?.InternalStorageMb != null || options?.Skin != null)
                {
                    ApplyAvdConfigSettings(name, options);
                }
                
                progress?.Report($"Successfully created emulator '{name}'");
                _logger.LogInformation($"Created AVD: {name}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to create AVD {name}: {ex.Message}", ex);
                progress?.Report($"Failed to create emulator: {ex.Message}");
                return false;
            }
        });
    }

    private void ApplyAvdConfigSettings(string avdName, EmulatorCreateOptions options)
    {
        try
        {
            // Find the AVD config.ini file
            var avdPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".android", "avd", $"{avdName}.avd", "config.ini");
            
            if (!File.Exists(avdPath))
            {
                _logger.LogWarning($"AVD config.ini not found at {avdPath}");
                return;
            }

            var lines = File.ReadAllLines(avdPath).ToList();
            
            if (options.RamSizeMb != null)
            {
                UpdateOrAddConfigLine(lines, "hw.ramSize", options.RamSizeMb.Value.ToString());
            }
            
            if (options.InternalStorageMb != null)
            {
                UpdateOrAddConfigLine(lines, "disk.dataPartition.size", $"{options.InternalStorageMb.Value}M");
            }
            
            if (!string.IsNullOrEmpty(options.Skin))
            {
                UpdateOrAddConfigLine(lines, "skin.name", options.Skin);
                // Also update the skin path if SDK path is known
                if (_sdkManager?.Home != null)
                {
                    var skinPath = System.IO.Path.Combine(_sdkManager.Home.FullName, "skins", options.Skin);
                    UpdateOrAddConfigLine(lines, "skin.path", skinPath);
                }
            }
            
            File.WriteAllLines(avdPath, lines);
            _logger.LogInformation($"Applied config settings to AVD {avdName}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to apply AVD config settings: {ex.Message}", ex);
        }
    }

    private void UpdateOrAddConfigLine(List<string> lines, string key, string value)
    {
        var index = lines.FindIndex(l => l.StartsWith($"{key}="));
        var newLine = $"{key}={value}";
        if (index >= 0)
            lines[index] = newLine;
        else
            lines.Add(newLine);
    }

    public Task<bool> DeleteAvdAsync(string name)
    {
        return Task.Run(() =>
        {
            if (_sdkManager == null)
            {
                _logger.LogError("SDK not initialized");
                return false;
            }

            try
            {
                _sdkManager.AvdManager.Delete(name);
                _logger.LogInformation($"Deleted AVD: {name}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to delete AVD {name}: {ex.Message}", ex);
                return false;
            }
        });
    }

    public Task<bool> StartEmulatorAsync(string avdName, bool coldBoot = false, IProgress<string>? progress = null)
    {
        return Task.Run(() =>
        {
            if (_sdkManager == null)
            {
                _logger.LogError("SDK not initialized");
                return false;
            }

            try
            {
                var bootType = coldBoot ? " (cold boot)" : "";
                progress?.Report($"Starting emulator '{avdName}'{bootType}...");
                
                var options = new AndroidSdk.Emulator.EmulatorStartOptions();
                if (coldBoot)
                {
                    options.NoSnapshotLoad = true;
                }
                
                var process = _sdkManager.Emulator.Start(avdName, options);
                progress?.Report($"Emulator '{avdName}' started");
                _logger.LogInformation($"Started emulator: {avdName}{bootType}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to start emulator {avdName}: {ex.Message}", ex);
                progress?.Report($"Failed to start emulator: {ex.Message}");
                return false;
            }
        });
    }

    public Task<bool> StopEmulatorAsync(string serial)
    {
        return Task.Run(() =>
        {
            if (_sdkManager == null)
            {
                _logger.LogError("SDK not initialized");
                return false;
            }

            try
            {
                _sdkManager.Adb.EmuKill(serial);
                _logger.LogInformation($"Stopped emulator: {serial}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to stop emulator {serial}: {ex.Message}", ex);
                return false;
            }
        });
    }
}

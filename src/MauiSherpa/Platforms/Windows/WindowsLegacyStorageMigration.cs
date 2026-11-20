using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Windows.ApplicationModel;
using Windows.Storage;

namespace MauiSherpa.Services;

internal static class WindowsLegacyStorageMigration
{
    private const string ApplicationId = "codes.redth.mauisherpa";
    private const string DefaultPublisherName = "Redth";
    private const string MigrationMarker = "MauiSherpa.LegacyUnpackagedStorageMigration.v1";
    private const string SecureStorageAlias = ApplicationId + ".microsoft.maui.essentials.preferences";

    private static readonly HashSet<string> DoublePreferenceKeys =
    [
        "window_width",
        "window_height"
    ];

    private static readonly HashSet<string> IntegerPreferenceKeys =
    [
        "local_vault_intro_version"
    ];

    private static readonly HashSet<string> BooleanPreferenceKeys =
    [
        "KeystoresSyncHintDismissed",
        "CertificatesSyncHintDismissed"
    ];

    private static readonly HashSet<string> IgnoredPreferenceKeys =
    [
        "LocalVault.AccessDenied",
        "LocalVault.AccessDeniedMessage",
        "LocalVault.AccessDeniedAtUtc"
    ];

    public static void TryMigrate()
    {
        if (!IsPackaged())
            return;

        try
        {
            var localSettings = ApplicationData.Current.LocalSettings;
            if (localSettings.Values.ContainsKey(MigrationMarker))
                return;

            var foundLegacyStorage = false;
            foreach (var settingsDirectory in GetLegacySettingsDirectories())
            {
                foundLegacyStorage |= MigrateSecureStorage(
                    Path.Combine(settingsDirectory, "securestorage.dat"),
                    localSettings);
                foundLegacyStorage |= MigratePreferences(
                    Path.Combine(settingsDirectory, "preferences.dat"),
                    localSettings);
            }

            if (foundLegacyStorage)
                localSettings.Values[MigrationMarker] = DateTimeOffset.UtcNow.ToString("O");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Legacy Windows storage migration failed: {ex}");
        }
    }

    private static bool IsPackaged()
    {
        try
        {
            return Package.Current != null;
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<string> GetLegacySettingsDirectories()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(WindowsLegacyStorageMigration).Assembly;
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value, StringComparer.Ordinal);

        metadata.TryGetValue("Microsoft.Maui.ApplicationModel.AppInfo.PublisherName", out var publisherName);
        metadata.TryGetValue("Microsoft.Maui.ApplicationModel.AppInfo.PackageName", out var packageName);

        var publishers = new[]
        {
            publisherName,
            assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company,
            DefaultPublisherName
        };
        var packages = new[]
        {
            packageName,
            assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title,
            ApplicationId
        };

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return publishers
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(publisher => packages
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(package => Path.Combine(
                    localAppData,
                    CleanPath(publisher!),
                    CleanPath(package!),
                    "Settings")))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static bool MigrateSecureStorage(string path, ApplicationDataContainer localSettings)
    {
        if (!File.Exists(path))
            return false;

        using var stream = File.OpenRead(path);
        var legacyValues = JsonSerializer.Deserialize<Dictionary<string, byte[]>>(stream)
            ?? throw new InvalidDataException($"Legacy secure storage is invalid: {path}");

        var target = localSettings.Containers.ContainsKey(SecureStorageAlias)
            ? localSettings.Containers[SecureStorageAlias]
            : localSettings.CreateContainer(SecureStorageAlias, ApplicationDataCreateDisposition.Always);

        foreach (var (key, value) in legacyValues)
        {
            if (!target.Values.ContainsKey(key))
                target.Values[key] = value;
        }

        return true;
    }

    private static bool MigratePreferences(string path, ApplicationDataContainer localSettings)
    {
        if (!File.Exists(path))
            return false;

        using var stream = File.OpenRead(path);
        var legacyPreferences = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream)
            ?? throw new InvalidDataException($"Legacy preferences are invalid: {path}");

        foreach (var (sharedName, values) in legacyPreferences)
        {
            var target = string.IsNullOrWhiteSpace(sharedName)
                ? localSettings
                : localSettings.Containers.ContainsKey(sharedName)
                    ? localSettings.Containers[sharedName]
                    : localSettings.CreateContainer(sharedName, ApplicationDataCreateDisposition.Always);

            foreach (var (key, value) in values)
            {
                if (!IgnoredPreferenceKeys.Contains(key) && !target.Values.ContainsKey(key))
                    target.Values[key] = ConvertPreferenceValue(key, value);
            }
        }

        return true;
    }

    private static object ConvertPreferenceValue(string key, string value)
    {
        if (DoublePreferenceKeys.Contains(key) &&
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
        {
            return doubleValue;
        }

        if (IntegerPreferenceKeys.Contains(key) &&
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integerValue))
        {
            return integerValue;
        }

        if (BooleanPreferenceKeys.Contains(key) &&
            bool.TryParse(value, out var booleanValue))
        {
            return booleanValue;
        }

        return value;
    }

    private static string CleanPath(string value) =>
        string.Join("_", value.Split(Path.GetInvalidFileNameChars()));
}

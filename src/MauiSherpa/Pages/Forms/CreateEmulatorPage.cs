using Microsoft.Maui.Controls;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Pages.Forms;

public record EmulatorCreateResult(
    string Name,
    string SystemImage,
    bool NeedsInstall,
    string? Device,
    int? RamSizeMb,
    int? InternalStorageMb,
    string? SdCardSize,
    string? Skin);

public class CreateEmulatorPage : FormPage<EmulatorCreateResult>
{
    private readonly List<AvdDeviceDefinition> _deviceDefinitions;
    private readonly List<string> _skins;

    // Parsed system image entries
    private readonly List<ImageEntry> _allImages;

    private Entry _nameEntry = null!;
    private Picker _apiLevelPicker = null!;
    private Picker _variantPicker = null!;
    private Picker _devicePicker = null!;
    private Picker _ramPicker = null!;
    private Picker _storagePicker = null!;
    private Picker _sdCardPicker = null!;
    private Picker _skinPicker = null!;

    // Current state
    private List<string> _apiLevels = new();
    private List<ImageEntry> _filteredVariants = new();

    private static readonly string[] RamLabels = { "(Default)", "1 GB", "2 GB", "3 GB", "4 GB" };
    private static readonly int?[] RamValues = { null, 1024, 2048, 3072, 4096 };
    private static readonly string[] StorageLabels = { "(Default)", "2 GB", "4 GB", "6 GB", "8 GB" };
    private static readonly int?[] StorageValues = { null, 2048, 4096, 6144, 8192 };
    private static readonly string[] SdCardLabels = { "None", "128 MB", "256 MB", "512 MB", "1 GB", "2 GB" };
    private static readonly string?[] SdCardValues = { null, "128M", "256M", "512M", "1G", "2G" };

    private static readonly Dictionary<string, string> VariantDisplayNames = new()
    {
        ["default"] = "Default (AOSP)",
        ["google_apis"] = "Google APIs",
        ["google_apis_tablet"] = "Google APIs (Tablet)",
        ["google_apis_playstore"] = "Google Play",
        ["google_apis_playstore_tablet"] = "Google Play (Tablet)",
        ["google_apis_playstore_ps16k"] = "Google Play (16K pages)",
        ["google_apis_ps16k"] = "Google APIs (16K pages)",
        ["google_atd"] = "Google ATD (Automated Test)",
        ["aosp_atd"] = "AOSP ATD (Automated Test)",
        ["android-tv"] = "Android TV",
        ["google-tv"] = "Google TV",
        ["google-xr"] = "Google XR",
        ["android-desktop"] = "Android Desktop",
        ["android-wear"] = "Wear OS",
        ["android-wear-cn"] = "Wear OS (China)",
        ["android-wear-signed"] = "Wear OS (Signed)",
        ["android-automotive"] = "Android Automotive",
        ["android-automotive-playstore"] = "Android Automotive (Play Store)",
        ["android-automotive-distant-display-playstore"] = "Android Automotive (Distant Display)",
    };

    protected override string FormTitle => "Create Emulator";

    protected override bool CanSubmit =>
        !string.IsNullOrWhiteSpace(_nameEntry?.Text)
        && _apiLevelPicker?.SelectedIndex >= 0
        && _variantPicker?.SelectedIndex >= 0;

    public CreateEmulatorPage(
        List<string> installedImages,
        List<string> availableImages,
        List<AvdDeviceDefinition> deviceDefinitions,
        List<string> skins)
    {
        _deviceDefinitions = deviceDefinitions;
        _skins = skins;

        // Parse all images into structured entries
        _allImages = new List<ImageEntry>();
        foreach (var img in installedImages)
        {
            var entry = ImageEntry.Parse(img, isInstalled: true);
            if (entry != null) _allImages.Add(entry);
        }
        foreach (var img in availableImages)
        {
            var entry = ImageEntry.Parse(img, isInstalled: false);
            if (entry != null) _allImages.Add(entry);
        }

        // Build sorted unique API levels (newest first)
        _apiLevels = _allImages
            .Select(i => i.ApiLevel)
            .Distinct()
            .OrderByDescending(ParseApiNumber)
            .ToList();
    }

    protected override View BuildFormContent()
    {
        _nameEntry = CreateEntry("My Emulator");
        _nameEntry.TextChanged += (_, _) => UpdateSubmitEnabled();

        // API Level picker
        var apiLabels = _apiLevels.Select(FormatApiLevel).ToList();
        _apiLevelPicker = CreatePicker("Select API level...", apiLabels);
        _apiLevelPicker.SelectedIndexChanged += OnApiLevelChanged;

        // Variant picker (populated when API level is selected)
        _variantPicker = CreatePicker("Select API level first...", Array.Empty<string>());
        _variantPicker.SelectedIndexChanged += (_, _) => UpdateSubmitEnabled();
        _variantPicker.IsEnabled = false;

        // Device definition picker
        var deviceLabels = new List<string> { "(Default)" };
        deviceLabels.AddRange(_deviceDefinitions.Select(d => d.Name));
        _devicePicker = CreatePicker(null, deviceLabels);
        _devicePicker.SelectedIndex = 0;

        _ramPicker = CreatePicker(null, RamLabels);
        _ramPicker.SelectedIndex = 0;

        _storagePicker = CreatePicker(null, StorageLabels);
        _storagePicker.SelectedIndex = 0;

        _sdCardPicker = CreatePicker(null, SdCardLabels);
        _sdCardPicker.SelectedIndex = 0;

        // Two-column grid for compact layout
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto), // Name
                new RowDefinition(GridLength.Auto), // API Level + Variant
                new RowDefinition(GridLength.Auto), // Device + RAM
                new RowDefinition(GridLength.Auto), // Storage + SD Card
            },
            ColumnSpacing = 16,
            RowSpacing = 16,
        };

        var nameGroup = CreateFormGroup("Emulator Name", _nameEntry);
        Grid.SetColumnSpan(nameGroup, 2);
        grid.Add(nameGroup, 0, 0);

        grid.Add(CreateFormGroup("API Level", _apiLevelPicker), 0, 1);
        grid.Add(CreateFormGroup("Image Variant", _variantPicker,
            "Available variants will be downloaded first"), 1, 1);

        grid.Add(CreateFormGroup("Device", _devicePicker), 0, 2);
        grid.Add(CreateFormGroup("RAM", _ramPicker), 1, 2);

        grid.Add(CreateFormGroup("Internal Storage", _storagePicker), 0, 3);
        grid.Add(CreateFormGroup("SD Card", _sdCardPicker), 1, 3);

        if (_skins.Count > 0)
        {
            var skinLabels = new List<string> { "(None)" };
            skinLabels.AddRange(_skins);
            _skinPicker = CreatePicker(null, skinLabels);
            _skinPicker.SelectedIndex = 0;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var skinGroup = CreateFormGroup("Skin", _skinPicker);
            Grid.SetColumnSpan(skinGroup, 2);
            grid.Add(skinGroup, 0, 4);
        }

        return grid;
    }

    private void OnApiLevelChanged(object? sender, EventArgs e)
    {
        if (_apiLevelPicker.SelectedIndex < 0)
        {
            _variantPicker.IsEnabled = false;
            _variantPicker.Title = "Select API level first...";
            _variantPicker.Items.Clear();
            _filteredVariants.Clear();
            UpdateSubmitEnabled();
            return;
        }

        var selectedApi = _apiLevels[_apiLevelPicker.SelectedIndex];

        // Filter images for this API level, preferring the host architecture
        var hostArch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture ==
            System.Runtime.InteropServices.Architecture.Arm64 ? "arm64-v8a" : "x86_64";

        _filteredVariants = _allImages
            .Where(i => i.ApiLevel == selectedApi)
            .Where(i => i.Arch == hostArch)
            .OrderBy(i => i.IsInstalled ? 0 : 1) // Installed first
            .ThenBy(i => i.Variant)
            .ToList();

        _variantPicker.Items.Clear();
        foreach (var img in _filteredVariants)
        {
            var displayName = GetVariantDisplayName(img.Variant);
            var suffix = img.IsInstalled ? " ✅" : " ⬇️";
            _variantPicker.Items.Add($"{displayName}{suffix}");
        }

        _variantPicker.IsEnabled = _filteredVariants.Count > 0;
        _variantPicker.Title = _filteredVariants.Count > 0
            ? "Select variant..."
            : "No images for this API level";
        _variantPicker.SelectedIndex = -1;
        UpdateSubmitEnabled();
    }

    protected override Task<EmulatorCreateResult> OnSubmitAsync()
    {
        var name = _nameEntry.Text.Trim().Replace(" ", "_");
        var selected = _filteredVariants[_variantPicker.SelectedIndex];

        string? device = null;
        if (_devicePicker.SelectedIndex > 0)
            device = _deviceDefinitions[_devicePicker.SelectedIndex - 1].Id;

        var ramMb = RamValues[_ramPicker.SelectedIndex];
        var storageMb = StorageValues[_storagePicker.SelectedIndex];
        var sdCard = SdCardValues[_sdCardPicker.SelectedIndex];

        string? skin = null;
        if (_skinPicker?.SelectedIndex > 0)
            skin = _skins[_skinPicker.SelectedIndex - 1];

        return Task.FromResult(new EmulatorCreateResult(
            name, selected.FullPath, !selected.IsInstalled, device, ramMb, storageMb, sdCard, skin));
    }

    private static string FormatApiLevel(string apiLevel)
    {
        // "android-35" → "API 35 (Android 15)"
        var num = ParseApiNumber(apiLevel);
        var androidVersion = num switch
        {
            36 => "Android 16",
            35 => "Android 15",
            34 => "Android 14",
            33 => "Android 13",
            32 => "Android 12L",
            31 => "Android 12",
            30 => "Android 11",
            29 => "Android 10",
            28 => "Android 9",
            27 => "Android 8.1",
            26 => "Android 8.0",
            25 => "Android 7.1",
            24 => "Android 7.0",
            _ => null,
        };
        return androidVersion != null ? $"API {num} ({androidVersion})" : $"API {num}";
    }

    private static string GetVariantDisplayName(string variant)
    {
        if (VariantDisplayNames.TryGetValue(variant, out var name))
            return name;

        // Smart fallback: parse unknown variants into friendly names
        // e.g. "google_apis_playstore_tablet" → "Google Apis Playstore Tablet"
        var words = variant.Replace('_', ' ').Replace('-', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
            words[i] = char.ToUpper(words[i][0]) + words[i][1..];
        return string.Join(" ", words);
    }

    private static int ParseApiNumber(string apiLevel)
        => int.TryParse(apiLevel.Replace("android-", ""), out var n) ? n : 0;

    private record ImageEntry(string FullPath, string ApiLevel, string Variant, string Arch, bool IsInstalled)
    {
        public static ImageEntry? Parse(string path, bool isInstalled)
        {
            // Format: system-images;android-XX;variant;arch
            var parts = path.Split(';');
            if (parts.Length < 4) return null;
            return new ImageEntry(path, parts[1], parts[2], parts[3], isInstalled);
        }
    }
}

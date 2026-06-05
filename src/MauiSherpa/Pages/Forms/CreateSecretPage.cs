using Microsoft.Maui.Controls;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Pages.Forms;

public record SecretCreateResult(
    string Key,
    string? Description,
    ManagedSecretType Type,
    byte[] Value,
    string? OriginalFileName,
    Dictionary<string, string> Metadata);

public class CreateSecretPage : FormPage<SecretCreateResult>
{
    private readonly string _initialFolderPath;
    private readonly IReadOnlyList<string> _folderPaths;
    private Picker _folderPicker = null!;
    private Entry _keyEntry = null!;
    private Entry _descriptionEntry = null!;
    private Picker _typePicker = null!;
    private Editor _valueEditor = null!;
    private Label _fileLabel = null!;
    private Button _fileButton = null!;
    private View _stringGroup = null!;
    private View _fileGroup = null!;
    private VerticalStackLayout _metadataRows = null!;
    private Label _metadataHint = null!;
    private Label _metadataError = null!;
    private readonly List<MetadataRow> _metadataEntries = new();

    private byte[]? _fileBytes;
    private string? _fileName;

    protected override string FormTitle => "Create Secret";

    public CreateSecretPage(string initialFolderPath = "/", IReadOnlyList<string>? folderPaths = null)
    {
        _initialFolderPath = SecretPath.NormalizeFolderPath(initialFolderPath);
        _folderPaths = new[] { "/" }
            .Concat(folderPaths ?? Array.Empty<string>())
            .Select(SecretPath.NormalizeFolderPath)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path == "/" ? "" : path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    protected override bool CanSubmit
    {
        get
        {
            if (_folderPicker?.SelectedIndex < 0) return false;
            if (!IsKeyValid()) return false;
            if (_typePicker?.SelectedIndex < 0) return false;
            if (!IsMetadataValid()) return false;
            if (_typePicker?.SelectedIndex == 0) // String
                return !string.IsNullOrWhiteSpace(_valueEditor?.Text);
            else // File
                return _fileBytes != null;
        }
    }

    protected override View BuildFormContent()
    {
        _folderPicker = CreatePicker(null, _folderPaths.Select(FormatFolderPath).ToList());
        var selectedFolderIndex = _folderPaths.ToList().FindIndex(path => path == _initialFolderPath);
        _folderPicker.SelectedIndex = selectedFolderIndex >= 0 ? selectedFolderIndex : 0;

        _keyEntry = CreateEntry("api-key");
        _keyEntry.TextChanged += (_, _) => UpdateSubmitEnabled();

        _descriptionEntry = CreateEntry("Optional description");

        _typePicker = CreatePicker(null, new[] { "String", "File" });
        _typePicker.SelectedIndex = 0;
        _typePicker.SelectedIndexChanged += (_, _) => OnTypeChanged();

        _valueEditor = new Editor
        {
            Placeholder = "Enter secret value",
            HeightRequest = 120,
            FontSize = 14,
            AutoSize = EditorAutoSizeOption.Disabled,
        };
        _valueEditor.SetDynamicResource(Editor.PlaceholderColorProperty, FormTheme.TextMuted);
        _valueEditor.SetDynamicResource(Editor.TextColorProperty, FormTheme.TextPrimary);
        _valueEditor.TextChanged += (_, _) => UpdateSubmitEnabled();

        _fileLabel = new Label
        {
            Text = "No file selected",
            FontSize = 13,
            VerticalOptions = LayoutOptions.Center,
        };
        _fileLabel.SetDynamicResource(Label.TextColorProperty, FormTheme.TextMuted);

        _fileButton = new Button
        {
            Text = "Choose File",
            BorderWidth = 1,
            CornerRadius = 6,
            FontSize = 13,
            HeightRequest = 36,
            Padding = new Thickness(12, 0),
        };
        _fileButton.SetDynamicResource(Button.BackgroundColorProperty, FormTheme.InputBg);
        _fileButton.SetDynamicResource(Button.TextColorProperty, FormTheme.TextPrimary);
        _fileButton.SetDynamicResource(Button.BorderColorProperty, FormTheme.InputBorder);
        _fileButton.Clicked += async (_, _) => await PickFileAsync();

        _stringGroup = CreateFormGroup("Value", _valueEditor);
        _fileGroup = CreateFormGroup("File", new HorizontalStackLayout
        {
            Spacing = 10,
            Children = { _fileButton, _fileLabel }
        });
        _fileGroup.IsVisible = false;

        _metadataRows = new VerticalStackLayout
        {
            Spacing = 8
        };

        _metadataHint = new Label
        {
            Text = "Add optional key/value metadata for this secret.",
            FontSize = 11,
        };
        _metadataHint.SetDynamicResource(Label.TextColorProperty, FormTheme.TextMuted);

        _metadataError = new Label
        {
            Text = "Metadata keys must be unique. A row with a value also needs a key.",
            FontSize = 11,
            IsVisible = false,
        };
        _metadataError.SetDynamicResource(Label.TextColorProperty, FormTheme.AccentDanger);

        var addMetadataButton = new Button
        {
            Text = "Add Metadata",
            BorderWidth = 0,
            CornerRadius = 6,
            FontSize = 13,
            HeightRequest = 34,
            Padding = new Thickness(12, 0),
            HorizontalOptions = LayoutOptions.Start,
        };
        addMetadataButton.SetDynamicResource(Button.BackgroundColorProperty, FormTheme.InputBg);
        addMetadataButton.SetDynamicResource(Button.TextColorProperty, FormTheme.TextPrimary);
        addMetadataButton.Clicked += (_, _) => AddMetadataRow();

        var metadataGroup = CreateFormGroup("Metadata", new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                _metadataHint,
                _metadataRows,
                _metadataError,
                addMetadataButton,
            }
        });

        return new VerticalStackLayout
        {
            Spacing = 16,
            Children =
            {
                CreateFormGroup("Folder", _folderPicker, "Create folders from the Secrets page, then choose one here"),
                CreateFormGroup("Key", _keyEntry, "Secret name within the selected folder"),
                CreateFormGroup("Description", _descriptionEntry),
                CreateFormGroup("Type", _typePicker),
                _stringGroup,
                _fileGroup,
                metadataGroup,
            }
        };
    }

    private void OnTypeChanged()
    {
        var isString = _typePicker.SelectedIndex == 0;
        _stringGroup.IsVisible = isString;
        _fileGroup.IsVisible = !isString;
        UpdateSubmitEnabled();
    }

    private async Task PickFileAsync()
    {
        try
        {
            var result = await FilePicker.Default.PickAsync();
            if (result != null)
            {
                _fileName = result.FileName;
                using var stream = await result.OpenReadAsync();
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                _fileBytes = ms.ToArray();
                _fileLabel.Text = $"{_fileName} ({_fileBytes.Length:N0} bytes)";
                UpdateSubmitEnabled();
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to read file: {ex.Message}", "OK");
        }
    }

    protected override Task<SecretCreateResult> OnSubmitAsync()
    {
        var path = new SecretPath(_folderPaths[_folderPicker.SelectedIndex], _keyEntry.Text);
        var key = path.ToFlatKey();
        var description = string.IsNullOrWhiteSpace(_descriptionEntry.Text) ? null : _descriptionEntry.Text.Trim();
        var type = _typePicker.SelectedIndex == 0 ? ManagedSecretType.String : ManagedSecretType.File;

        byte[] value;
        string? originalFileName = null;

        if (type == ManagedSecretType.String)
        {
            value = System.Text.Encoding.UTF8.GetBytes(_valueEditor.Text);
        }
        else
        {
            value = _fileBytes!;
            originalFileName = _fileName;
        }

        return Task.FromResult(new SecretCreateResult(
            key,
            description,
            type,
            value,
            originalFileName,
            GetMetadata()));
    }

    private bool IsKeyValid()
    {
        try
        {
            _ = SecretPath.NormalizeKey(_keyEntry?.Text ?? "");
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string FormatFolderPath(string folderPath) => folderPath == "/" ? "Root (/)" : folderPath;

    private void AddMetadataRow(string key = "", string value = "")
    {
        var keyEntry = CreateEntry("metadata-key");
        keyEntry.Text = key;
        var valueEntry = CreateEntry("metadata value");
        valueEntry.Text = value;

        var removeButton = new Button
        {
            Text = "Remove",
            BorderWidth = 0,
            CornerRadius = 6,
            FontSize = 13,
            HeightRequest = 36,
            Padding = new Thickness(10, 0),
            VerticalOptions = LayoutOptions.Center,
        };
        removeButton.SetDynamicResource(Button.BackgroundColorProperty, FormTheme.InputBg);
        removeButton.SetDynamicResource(Button.TextColorProperty, FormTheme.AccentDanger);

        var rowLayout = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            ColumnSpacing = 8,
        };
        Grid.SetColumn(keyEntry, 0);
        Grid.SetColumn(valueEntry, 1);
        Grid.SetColumn(removeButton, 2);
        rowLayout.Children.Add(keyEntry);
        rowLayout.Children.Add(valueEntry);
        rowLayout.Children.Add(removeButton);

        var row = new MetadataRow(keyEntry, valueEntry, rowLayout);
        removeButton.Clicked += (_, _) =>
        {
            _metadataEntries.Remove(row);
            _metadataRows.Children.Remove(rowLayout);
            UpdateMetadataHint();
            UpdateMetadataError();
            UpdateSubmitEnabled();
        };

        keyEntry.TextChanged += (_, _) =>
        {
            UpdateMetadataError();
            UpdateSubmitEnabled();
        };
        valueEntry.TextChanged += (_, _) =>
        {
            UpdateMetadataError();
            UpdateSubmitEnabled();
        };

        _metadataEntries.Add(row);
        _metadataRows.Children.Add(rowLayout);
        UpdateMetadataHint();
        UpdateMetadataError();
        UpdateSubmitEnabled();
    }

    private void UpdateMetadataHint()
    {
        _metadataHint.Text = _metadataEntries.Count == 0
            ? "Add optional key/value metadata for this secret."
            : "Leave a value empty to store an empty value. Remove a row to delete it.";
    }

    private void UpdateMetadataError()
    {
        _metadataError.IsVisible = !IsMetadataValid();
    }

    private bool IsMetadataValid()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in _metadataEntries)
        {
            var key = row.KeyEntry.Text?.Trim() ?? "";
            var value = row.ValueEntry.Text ?? "";
            if (string.IsNullOrEmpty(key))
            {
                if (!string.IsNullOrEmpty(value))
                    return false;
                continue;
            }

            if (!keys.Add(key))
                return false;
        }

        return true;
    }

    private Dictionary<string, string> GetMetadata()
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in _metadataEntries)
        {
            var key = row.KeyEntry.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(key))
                continue;

            metadata[key] = row.ValueEntry.Text ?? "";
        }

        return metadata;
    }

    private sealed record MetadataRow(Entry KeyEntry, Entry ValueEntry, View Layout);
}

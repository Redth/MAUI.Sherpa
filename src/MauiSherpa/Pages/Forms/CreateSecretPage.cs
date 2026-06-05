using Microsoft.Maui.Controls;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Pages.Forms;

public record SecretCreateResult(string Key, string? Description, ManagedSecretType Type, byte[] Value, string? OriginalFileName);

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

        return Task.FromResult(new SecretCreateResult(key, description, type, value, originalFileName));
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
}

using Microsoft.Maui.Controls;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Pages.Forms;

public record SecretCreateResult(string Key, string? Description, ManagedSecretType Type, byte[] Value, string? OriginalFileName);

public class CreateSecretPage : FormPage<SecretCreateResult>
{
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

    protected override bool CanSubmit
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_keyEntry?.Text)) return false;
            if (_typePicker?.SelectedIndex < 0) return false;
            if (_typePicker?.SelectedIndex == 0) // String
                return !string.IsNullOrWhiteSpace(_valueEditor?.Text);
            else // File
                return _fileBytes != null;
        }
    }

    protected override View BuildFormContent()
    {
        _keyEntry = CreateEntry("my-secret-key");
        _keyEntry.TextChanged += (_, _) => UpdateSubmitEnabled();

        _descriptionEntry = CreateEntry("Optional description");

        _typePicker = CreatePicker(null, new[] { "String", "File" });
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
                CreateFormGroup("Key", _keyEntry, "Unique identifier for this secret"),
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
        var key = _keyEntry.Text.Trim();
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
}

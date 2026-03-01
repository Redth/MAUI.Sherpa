using Microsoft.Maui.Controls;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Pages.Forms;

public record KeystoreCreateResult(
    string Alias,
    string KeystorePassword,
    string KeyPassword,
    string CN,
    string OU,
    string Organization,
    string City,
    string State,
    string Country,
    int ValidityDays,
    string KeyAlgorithm,
    int KeySize);

public class CreateKeystorePage : FormPage<KeystoreCreateResult>
{
    private Entry _aliasEntry = null!;
    private Entry _keystorePasswordEntry = null!;
    private Entry _keyPasswordEntry = null!;
    private Entry _cnEntry = null!;
    private Entry _ouEntry = null!;
    private Entry _orgEntry = null!;
    private Entry _cityEntry = null!;
    private Entry _stateEntry = null!;
    private Entry _countryEntry = null!;
    private Entry _validityEntry = null!;
    private Picker _algorithmPicker = null!;
    private Picker _keySizePicker = null!;

    private static readonly string[] AlgorithmLabels = { "RSA", "DSA", "EC" };
    private static readonly string[] KeySizeLabels = { "2048", "3072", "4096" };
    private static readonly int[] KeySizeValues = { 2048, 3072, 4096 };

    protected override string FormTitle => "Create Keystore";

    protected override bool CanSubmit
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_aliasEntry?.Text)) return false;
            if ((_keystorePasswordEntry?.Text?.Length ?? 0) < 6) return false;
            if ((_keyPasswordEntry?.Text?.Length ?? 0) < 6) return false;
            return true;
        }
    }

    protected override View BuildFormContent()
    {
        _aliasEntry = CreateEntry("mykey");
        _aliasEntry.TextChanged += (_, _) => UpdateSubmitEnabled();

        _keystorePasswordEntry = CreatePasswordEntry("Min 6 characters");
        _keystorePasswordEntry.TextChanged += (_, _) => UpdateSubmitEnabled();

        _keyPasswordEntry = CreatePasswordEntry("Min 6 characters");
        _keyPasswordEntry.TextChanged += (_, _) => UpdateSubmitEnabled();

        _cnEntry = CreateEntry("Your Name");
        _ouEntry = CreateEntry("Development");
        _orgEntry = CreateEntry("Company");
        _cityEntry = CreateEntry("City");
        _stateEntry = CreateEntry("State");
        _countryEntry = CreateEntry("US");
        _countryEntry.MaxLength = 2;

        _validityEntry = CreateEntry("10000");
        _validityEntry.Keyboard = Keyboard.Numeric;
        _validityEntry.Text = "10000";

        _algorithmPicker = CreatePicker(null, AlgorithmLabels);
        _algorithmPicker.SelectedIndex = 0;

        _keySizePicker = CreatePicker(null, KeySizeLabels);
        _keySizePicker.SelectedIndex = 0;

        // Top section: Alias left, passwords stacked right
        var authGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
            },
            ColumnSpacing = 16,
            RowSpacing = 16,
        };
        authGrid.Add(CreateFormGroup("Alias", _aliasEntry), 0, 0);
        authGrid.Add(CreateFormGroup("Keystore Password", _keystorePasswordEntry), 1, 0);
        authGrid.Add(CreateFormGroup("Key Password", _keyPasswordEntry), 1, 1);

        var certGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto), // CN + Org
                new RowDefinition(GridLength.Auto), // OU + City
                new RowDefinition(GridLength.Auto), // State + Country
            },
            ColumnSpacing = 16,
            RowSpacing = 16,
        };
        certGrid.Add(CreateFormGroup("Common Name (CN)", _cnEntry), 0, 0);
        certGrid.Add(CreateFormGroup("Organization (O)", _orgEntry), 1, 0);
        certGrid.Add(CreateFormGroup("Org Unit (OU)", _ouEntry), 0, 1);
        certGrid.Add(CreateFormGroup("City (L)", _cityEntry), 1, 1);
        certGrid.Add(CreateFormGroup("State (ST)", _stateEntry), 0, 2);
        certGrid.Add(CreateFormGroup("Country (C)", _countryEntry, "2-letter code"), 1, 2);

        var certSection = new VerticalStackLayout
        {
            Spacing = 16,
            Children =
            {
                CreateSectionHeader("Certificate Information"),
                certGrid,
            }
        };

        var keyGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
            },
            ColumnSpacing = 16,
        };
        keyGrid.Add(CreateFormGroup("Validity (days)", _validityEntry), 0, 0);
        keyGrid.Add(CreateFormGroup("Algorithm", _algorithmPicker), 1, 0);
        keyGrid.Add(CreateFormGroup("Key Size", _keySizePicker), 2, 0);

        var keySection = new VerticalStackLayout
        {
            Spacing = 16,
            Children =
            {
                CreateSectionHeader("Key Configuration"),
                keyGrid,
            }
        };

        return new VerticalStackLayout
        {
            Spacing = 24,
            Children = { authGrid, certSection, keySection }
        };
    }

    private View CreateSectionHeader(string title)
    {
        var headerLabel = new Label
        {
            Text = title,
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
        };
        headerLabel.SetDynamicResource(Label.TextColorProperty, FormTheme.AccentPrimary);

        var headerLine = new BoxView
        {
            HeightRequest = 1,
            Margin = new Thickness(0, 2, 0, 4),
        };
        headerLine.SetDynamicResource(BoxView.ColorProperty, FormTheme.Separator);

        return new VerticalStackLayout
        {
            Spacing = 4,
            Children = { headerLabel, headerLine }
        };
    }

    protected override Task<KeystoreCreateResult> OnSubmitAsync()
    {
        var validity = int.TryParse(_validityEntry.Text, out var v) ? v : 10000;
        var keySize = KeySizeValues[_keySizePicker.SelectedIndex];
        var algorithm = AlgorithmLabels[_algorithmPicker.SelectedIndex];

        return Task.FromResult(new KeystoreCreateResult(
            Alias: _aliasEntry.Text.Trim(),
            KeystorePassword: _keystorePasswordEntry.Text,
            KeyPassword: _keyPasswordEntry.Text,
            CN: _cnEntry.Text?.Trim() ?? "",
            OU: _ouEntry.Text?.Trim() ?? "",
            Organization: _orgEntry.Text?.Trim() ?? "",
            City: _cityEntry.Text?.Trim() ?? "",
            State: _stateEntry.Text?.Trim() ?? "",
            Country: _countryEntry.Text?.Trim() ?? "",
            ValidityDays: validity,
            KeyAlgorithm: algorithm,
            KeySize: keySize));
    }
}

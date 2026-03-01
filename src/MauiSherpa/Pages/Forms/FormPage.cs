using Microsoft.Maui.Controls;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Pages.Forms;

public interface IFormPageBuildable
{
    void EnsureBuilt();
}

/// <summary>
/// Base class for native MAUI form modal pages.
/// Provides themed form layout with title, scrollable body, and Cancel/Submit footer.
/// Uses DynamicResource bindings from FormTheme for light/dark mode support.
/// Subclasses override BuildFormContent() and OnSubmitAsync().
/// </summary>
public abstract class FormPage<TResult> : ContentPage, IFormPage<TResult>, IFormPageBuildable
{
    private readonly TaskCompletionSource<TResult?> _tcs = new();
    private Button? _submitButton;
    private ActivityIndicator? _submittingIndicator;
    private bool _isSubmitting;

    protected abstract string FormTitle { get; }
    protected virtual string SubmitButtonText => "Create";

    /// <summary>Build the form fields. Return a View containing all form inputs.</summary>
    protected abstract View BuildFormContent();

    /// <summary>Called when the user clicks Submit. Return the result value.</summary>
    protected abstract Task<TResult> OnSubmitAsync();

    /// <summary>Override to control when the submit button is enabled.</summary>
    protected virtual bool CanSubmit => true;

    public FormPage()
    {

#if MACOSAPP
        Microsoft.Maui.Platform.MacOS.MacOSPage.SetModalSheetSizesToContent(this, true);
        Microsoft.Maui.Platform.MacOS.MacOSPage.SetModalSheetMinWidth(this, 420);
#endif
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        EnsureBuilt();
    }

    /// <summary>Build the page content if not already built. Called before modal push.</summary>
    public void EnsureBuilt()
    {
        if (Content == null)
            BuildPage();
    }

    public Task<TResult?> GetResultAsync() => _tcs.Task;

    private void BuildPage()
    {
        // Title
        var titleLabel = new Label
        {
            Text = FormTitle,
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            Margin = new Thickness(28, 24, 28, 12),
        };

        // Header separator — full width
        var headerSeparator = new BoxView { HeightRequest = 1, Opacity = 0.2 };


        var formContent = BuildFormContent();
        formContent.Margin = new Thickness(28, 16, 28, 16);

        // Footer separator — full width
        var footerSeparator = new BoxView { HeightRequest = 1, Opacity = 0.2 };


        var cancelButton = new Button
        {
            Text = "Cancel",
            FontSize = 13,
            BackgroundColor = Colors.Transparent,
            BorderWidth = 0,
            CornerRadius = 5,
            Padding = new Thickness(14, 4),
            HeightRequest = 30,
        };
        cancelButton.SetDynamicResource(Button.TextColorProperty, FormTheme.AccentPrimary);
        cancelButton.Clicked += OnCancelClicked;

        _submittingIndicator = new ActivityIndicator
        {
            IsRunning = false,
            IsVisible = false,
            WidthRequest = 16,
            HeightRequest = 16,
            VerticalOptions = LayoutOptions.Center,
        };
        _submittingIndicator.SetDynamicResource(ActivityIndicator.ColorProperty, FormTheme.AccentPrimary);

        _submitButton = new Button
        {
            Text = SubmitButtonText,
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            CornerRadius = 5,
            Padding = new Thickness(14, 4),
            HeightRequest = 30,
            IsEnabled = CanSubmit,
        };
        _submitButton.SetDynamicResource(Button.BackgroundColorProperty, FormTheme.AccentPrimary);
        _submitButton.Clicked += OnSubmitClicked;

        var footerLayout = new HorizontalStackLayout
        {
            Spacing = 12,
            HorizontalOptions = LayoutOptions.End,
            Margin = new Thickness(28, 12, 28, 24),
            Children = { cancelButton, _submittingIndicator, _submitButton },
        };

        Content = new VerticalStackLayout
        {
            Spacing = 0,
            Children =
            {
                titleLabel,
                headerSeparator,
                formContent,
                footerSeparator,
                footerLayout,
            },
        };
    }

    /// <summary>Call this from subclasses when form validity changes.</summary>
    protected void UpdateSubmitEnabled()
    {
        if (_submitButton != null)
            _submitButton.IsEnabled = CanSubmit && !_isSubmitting;
    }

    private async void OnSubmitClicked(object? sender, EventArgs e)
    {
        if (_isSubmitting) return;
        _isSubmitting = true;
        _submitButton!.IsEnabled = false;
        _submitButton.Text = "Creating...";
        _submittingIndicator!.IsRunning = true;
        _submittingIndicator.IsVisible = true;

        try
        {
            var result = await OnSubmitAsync();
            _tcs.TrySetResult(result);
        }
        catch (Exception ex)
        {
            _isSubmitting = false;
            _submitButton.Text = SubmitButtonText;
            _submitButton.IsEnabled = CanSubmit;
            _submittingIndicator.IsRunning = false;
            _submittingIndicator.IsVisible = false;

            await DisplayAlert("Error", ex.Message, "OK");
        }
    }

    private void OnCancelClicked(object? sender, EventArgs e)
    {
        _tcs.TrySetResult(default);
    }

    // --- Helper methods for building form fields ---

    protected View CreateFormGroup(string label, View input, string? helpText = null)
    {
        var stack = new VerticalStackLayout { Spacing = 6 };

        var labelView = new Label
        {
            Text = label,
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
        };
        labelView.SetDynamicResource(Label.TextColorProperty, FormTheme.TextSecondary);
        stack.Children.Add(labelView);

        stack.Children.Add(input);

        if (!string.IsNullOrEmpty(helpText))
        {
            var helpLabel = new Label
            {
                Text = helpText,
                FontSize = 11,
            };
            helpLabel.SetDynamicResource(Label.TextColorProperty, FormTheme.TextMuted);
            stack.Children.Add(helpLabel);
        }

        return stack;
    }

    protected Entry CreateEntry(string placeholder = "", Keyboard? keyboard = null)
    {
        var entry = new Entry
        {
            Placeholder = placeholder,
            FontSize = 14,
            Keyboard = keyboard ?? Keyboard.Default,
        };
        entry.SetDynamicResource(Entry.PlaceholderColorProperty, FormTheme.TextMuted);
        entry.SetDynamicResource(Entry.TextColorProperty, FormTheme.TextPrimary);
        entry.TextChanged += (_, _) => UpdateSubmitEnabled();
        return entry;
    }

    protected Entry CreatePasswordEntry(string placeholder = "")
    {
        var entry = CreateEntry(placeholder);
        entry.IsPassword = true;
        return entry;
    }

    protected Picker CreatePicker(string? title, IList<string> items)
    {
        var picker = new Picker
        {
            Title = title,
            FontSize = 14,
        };
        picker.SetDynamicResource(Picker.TitleColorProperty, FormTheme.TextMuted);
        picker.SetDynamicResource(Picker.TextColorProperty, FormTheme.TextPrimary);
        foreach (var item in items)
            picker.Items.Add(item);
        picker.SelectedIndexChanged += (_, _) => UpdateSubmitEnabled();
        return picker;
    }

    protected Editor CreateEditor(string placeholder = "", int maxHeight = 120)
    {
        var editor = new Editor
        {
            Placeholder = placeholder,
            FontSize = 14,
            AutoSize = EditorAutoSizeOption.TextChanges,
            MaximumHeightRequest = maxHeight,
        };
        editor.SetDynamicResource(Editor.PlaceholderColorProperty, FormTheme.TextMuted);
        editor.SetDynamicResource(Editor.TextColorProperty, FormTheme.TextPrimary);
        return editor;
    }
}

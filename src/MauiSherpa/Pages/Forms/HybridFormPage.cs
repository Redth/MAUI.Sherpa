using Microsoft.Maui.Controls;
using MauiSherpa.Core.Interfaces;
#if MACOSAPP
using Microsoft.Maui.Platforms.MacOS.Platform;
using Microsoft.Maui.Platforms.MacOS.Controls;
#else
using Microsoft.AspNetCore.Components.WebView.Maui;
#endif
#if LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Forms;

/// <summary>
/// Base class for hybrid form modal pages.
/// Uses native MAUI title and buttons with a BlazorWebView for complex form content.
/// Grid layout: Row 0 = native title, Row 1 = BlazorWebView, Row 2 = native footer.
/// Subclasses specify the Blazor route and submit button text.
/// The embedded Blazor component communicates via HybridFormBridge.
/// </summary>
public abstract class HybridFormPage<TResult> : ContentPage, IFormPage<TResult>, IFormPageBuildable
{
    private readonly TaskCompletionSource<TResult?> _tcs = new();
    private readonly HybridFormBridge _bridge = new();
    private readonly HybridFormBridgeHolder _bridgeHolder;
    private Button? _submitButton;
    private Button? _cancelButton;
    private ActivityIndicator? _submittingIndicator;
    private bool _isSubmitting;
    private bool _isClosed;
    private View? _webView;

    /// <summary>Page title shown in the native header.</summary>
    protected abstract string FormTitle { get; }

    /// <summary>Submit button text (e.g. "Save", "Export", "Create").</summary>
    protected virtual string SubmitButtonText => "Save";

    /// <summary>Height of the native footer actions.</summary>
    protected virtual double ActionButtonHeight => 30;

    /// <summary>Blazor route for the form content (e.g. "/modal/edit-secret").</summary>
    protected abstract string BlazorRoute { get; }

    /// <summary>The bridge for communicating with the embedded Blazor component.</summary>
    public HybridFormBridge Bridge => _bridge;

    public HybridFormPage(HybridFormBridgeHolder bridgeHolder)
    {
        _bridgeHolder = bridgeHolder;

#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, true);
        MacOSPage.SetModalSheetMinWidth(this, 500);
#elif LINUXGTK
        GtkPage.SetModalSizesToContent(this, true);
        GtkPage.SetModalMinWidth(this, 500);
#endif
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        EnsureBuilt();
    }

    public void EnsureBuilt()
    {
        if (Content == null)
            BuildPage();
    }

    public Task<TResult?> GetResultAsync() => _tcs.Task;

    /// <summary>
    /// Override to pass parameters to the Blazor component via the bridge.
    /// Called before the BlazorWebView is created.
    /// </summary>
    protected virtual void ConfigureBridge(HybridFormBridge bridge) { }

    private void BuildPage()
    {
        // Set up bridge
        ConfigureBridge(_bridge);
        _bridgeHolder.Push(_bridge);
        _bridge.ValidationChanged += () =>
            Dispatcher.Dispatch(() =>
            {
                if (_submitButton != null && !_bridge.HasActionState)
                    _submitButton.IsEnabled = _bridge.IsValid && !_isSubmitting;
            });
        _bridge.ActionStateChanged += OnActionStateChanged;
        _bridge.CloseRequested += OnCloseRequested;

        // Title
        var titleLabel = new Label
        {
            Text = FormTitle,
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            Margin = new Thickness(28, 24, 28, 12),
        };

        // Header separator — full width
        var headerSeparator = new BoxView { HeightRequest = 1 };
        headerSeparator.SetDynamicResource(BoxView.ColorProperty, FormTheme.Separator);


        // BlazorWebView
        ModalWebViewReadySignal.Ready += OnBlazorReady;
        View webView;
#if MACOSAPP
        var blazorWebView = new MacOSBlazorWebView
        {
            HostPage = "wwwroot/index.html",
            StartPath = BlazorRoute,
            Opacity = 0,
        };
        blazorWebView.RootComponents.Add(new BlazorRootComponent
        {
            Selector = "#app",
            ComponentType = typeof(Components.ModalApp)
        });
        webView = blazorWebView;
#else
        var blazorWebView = new BlazorWebView
        {
            HostPage = "wwwroot/index.html",
            StartPath = BlazorRoute,
            Opacity = 0,
        };
        blazorWebView.RootComponents.Add(new RootComponent
        {
            Selector = "#app",
            ComponentType = typeof(Components.ModalApp)
        });
        HandlerProperties.SetDisconnectPolicy(blazorWebView, HandlerDisconnectPolicy.Manual);
        webView = blazorWebView;
#endif
        _webView = webView;

        // Footer separator — full width
        var footerSeparator = new BoxView { HeightRequest = 1 };
        footerSeparator.SetDynamicResource(BoxView.ColorProperty, FormTheme.Separator);


        _cancelButton = new Button
        {
            Text = "Cancel",
            FontSize = 13,
            BackgroundColor = Colors.Transparent,
            BorderWidth = 0,
            CornerRadius = 5,
            Padding = new Thickness(14, 4),
            HeightRequest = ActionButtonHeight,
        };
        _cancelButton.SetDynamicResource(Button.TextColorProperty, FormTheme.AccentPrimary);
        _cancelButton.Clicked += OnCancelClicked;

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
            HeightRequest = ActionButtonHeight,
            IsEnabled = false,
        };
        _submitButton.SetDynamicResource(Button.BackgroundColorProperty, FormTheme.AccentPrimary);
        _submitButton.Clicked += OnSubmitClicked;

        var footerLayout = new HorizontalStackLayout
        {
            Spacing = 12,
            HorizontalOptions = LayoutOptions.End,
            Margin = new Thickness(28, 12, 28, 24),
            Children = { _cancelButton, _submittingIndicator, _submitButton },
        };

        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),   // 0: title
                new RowDefinition(GridLength.Auto),   // 1: header separator
                new RowDefinition(GridLength.Star),   // 2: webview
                new RowDefinition(GridLength.Auto),   // 3: footer separator
                new RowDefinition(GridLength.Auto),   // 4: buttons
            },
            Padding = 0,
            RowSpacing = 0,
            VerticalOptions = LayoutOptions.Fill,
        };
        grid.SetDynamicResource(Grid.BackgroundColorProperty, FormTheme.PageBg);

        Grid.SetRow(titleLabel, 0);
        Grid.SetRow(headerSeparator, 1);
        Grid.SetRow(webView, 2);
        Grid.SetRow(footerSeparator, 3);
        Grid.SetRow(footerLayout, 4);

        grid.Children.Add(titleLabel);
        grid.Children.Add(headerSeparator);
        grid.Children.Add(webView);
        grid.Children.Add(footerSeparator);
        grid.Children.Add(footerLayout);

        Content = grid;
        ApplyActionState();
    }

    private async void OnSubmitClicked(object? sender, EventArgs e)
    {
        if (_isSubmitting) return;
        _isSubmitting = true;
        _submitButton!.IsEnabled = false;
        _submitButton.Text = "Saving...";
        _submittingIndicator!.IsRunning = true;
        _submittingIndicator.IsVisible = true;

        try
        {
            await _bridge.RequestSubmitAsync();
            if (_bridge.PreventSubmitClose)
            {
                _isSubmitting = false;
                ApplyActionState();
                return;
            }

            var result = (TResult?)_bridge.Result;
            CloseWithResult(result);
        }
        catch (Exception ex)
        {
            _isSubmitting = false;
            if (_bridge.HasActionState)
            {
                ApplyActionState();
            }
            else
            {
                _submitButton.Text = SubmitButtonText;
                _submitButton.IsEnabled = _bridge.IsValid;
                _submittingIndicator.IsRunning = false;
                _submittingIndicator.IsVisible = false;
            }

            await DisplayAlert("Error", ex.Message, "OK");
        }
    }

    private void OnCancelClicked(object? sender, EventArgs e)
    {
        _bridge.RequestCancel();
        if (_bridge.PreventClose)
        {
            ApplyActionState();
            return;
        }

        CloseWithResult(default);
    }

    private void OnActionStateChanged()
    {
        if (Dispatcher.IsDispatchRequired)
            Dispatcher.Dispatch(ApplyActionState);
        else
            ApplyActionState();
    }

    private void ApplyActionState()
    {
        if (!_bridge.HasActionState)
            return;

        if (_submitButton != null)
        {
            _submitButton.Text = _bridge.ActionText ?? SubmitButtonText;
            _submitButton.IsEnabled = _bridge.ActionEnabled && !_bridge.ActionBusy && !_isSubmitting;
        }

        if (_cancelButton != null)
            _cancelButton.Text = _bridge.SecondaryActionText ?? "Cancel";

        if (_submittingIndicator != null)
        {
            _submittingIndicator.IsRunning = _bridge.ActionBusy;
            _submittingIndicator.IsVisible = _bridge.ActionBusy;
        }
    }

    private void OnCloseRequested()
    {
        Dispatcher.Dispatch(() =>
        {
            var result = (TResult?)_bridge.Result;
            CloseWithResult(result);
        });
    }

    private void CloseWithResult(TResult? result)
    {
        if (_isClosed)
            return;

        _isClosed = true;
        _bridge.ActionStateChanged -= OnActionStateChanged;
        _bridge.CloseRequested -= OnCloseRequested;
        _bridgeHolder.Pop();
        _tcs.TrySetResult(result);
    }

    private void OnBlazorReady(double contentHeight)
    {
        ModalWebViewReadySignal.Ready -= OnBlazorReady;
        Dispatcher.Dispatch(async () =>
        {
            if (_webView != null)
            {
                _webView.HeightRequest = Math.Max(contentHeight, 200);
                await _webView.FadeToAsync(1, 200, Easing.CubicIn);
            }
        });
    }
}

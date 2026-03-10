using Microsoft.Maui.Controls;
using MauiSherpa.Core.Interfaces;
#if MACOSAPP
using Microsoft.Maui.Platform.MacOS;
using Microsoft.Maui.Platform.MacOS.Controls;
#else
using Microsoft.AspNetCore.Components.WebView.Maui;
#endif
#if LINUXGTK
using Platform.Maui.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Forms;

/// <summary>
/// Base class for wizard-style hybrid modal pages with dynamic native buttons.
/// Grid layout: Row 0 = native title, Row 1 = header separator, Row 2 = BlazorWebView,
/// Row 3 = footer separator, Row 4 = native footer with Back/Cancel/Next/Submit.
/// The Blazor component controls button visibility via HybridFormBridge.SetWizardState().
/// </summary>
public abstract class WizardFormPage<TResult> : ContentPage, IFormPage<TResult>, IFormPageBuildable
{
    private readonly TaskCompletionSource<TResult?> _tcs = new();
    private readonly HybridFormBridge _bridge = new();
    private readonly HybridFormBridgeHolder _bridgeHolder;
    private Button? _backButton;
    private Button? _primaryButton;
    private ActivityIndicator? _submittingIndicator;
    private View? _webView;
    private bool _isOnLastStep;

    protected abstract string FormTitle { get; }
    protected virtual string DefaultSubmitText => "Create";
    protected abstract string BlazorRoute { get; }

    public HybridFormBridge Bridge => _bridge;

    public WizardFormPage(HybridFormBridgeHolder bridgeHolder)
    {
        _bridgeHolder = bridgeHolder;
        this.SetDynamicResource(BackgroundColorProperty, FormTheme.PageBg);
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

    private void BuildPage()
    {
        _bridgeHolder.Push(_bridge);

        // Listen for wizard state changes from Blazor
        _bridge.WizardStateChanged += OnWizardStateChanged;
        _bridge.CloseRequested += OnCloseRequested;

        // Title
        var titleLabel = new Label
        {
            Text = FormTitle,
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            Margin = new Thickness(28, 24, 28, 12),
        };
        titleLabel.SetDynamicResource(Label.TextColorProperty, FormTheme.TextPrimary);

        // Header separator
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

        // Footer separator
        var footerSeparator = new BoxView { HeightRequest = 1 };
        footerSeparator.SetDynamicResource(BoxView.ColorProperty, FormTheme.Separator);

        // Cancel button (left side)
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

        // Back button (right side, beside primary)
        _backButton = new Button
        {
            Text = "← Back",
            FontSize = 13,
            BackgroundColor = Colors.Transparent,
            BorderWidth = 0,
            CornerRadius = 5,
            Padding = new Thickness(14, 4),
            HeightRequest = 30,
        };
        _backButton.SetDynamicResource(Button.TextColorProperty, FormTheme.TextSecondary);
        _backButton.Clicked += OnBackClicked;

        // Submitting indicator
        _submittingIndicator = new ActivityIndicator
        {
            IsRunning = false,
            IsVisible = false,
            WidthRequest = 16,
            HeightRequest = 16,
            VerticalOptions = LayoutOptions.Center,
        };
        _submittingIndicator.SetDynamicResource(ActivityIndicator.ColorProperty, FormTheme.AccentPrimary);

        // Primary button — changes text between "Next →" and submit text
        _primaryButton = new Button
        {
            Text = "Next →",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            CornerRadius = 5,
            Padding = new Thickness(14, 4),
            HeightRequest = 30,
            IsEnabled = false,
        };
        _primaryButton.SetDynamicResource(Button.BackgroundColorProperty, FormTheme.AccentPrimary);
        _primaryButton.Clicked += OnPrimaryClicked;

        // Footer layout: Cancel on left, Back + Primary on right
        var rightButtons = new HorizontalStackLayout
        {
            Spacing = 12,
            HorizontalOptions = LayoutOptions.End,
            Children = { _backButton, _submittingIndicator, _primaryButton },
        };

        var footerGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
            Margin = new Thickness(28, 12, 28, 24),
        };
        Grid.SetColumn(cancelButton, 0);
        Grid.SetColumn(rightButtons, 1);
        footerGrid.Children.Add(cancelButton);
        footerGrid.Children.Add(rightButtons);

        // Main grid
        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),   // 0: title
                new RowDefinition(GridLength.Auto),   // 1: header separator
                new RowDefinition(GridLength.Star),   // 2: webview
                new RowDefinition(GridLength.Auto),   // 3: footer separator
                new RowDefinition(GridLength.Auto),   // 4: footer buttons
            },
            Padding = 0,
            RowSpacing = 0,
            VerticalOptions = LayoutOptions.Fill,
        };

        Grid.SetRow(titleLabel, 0);
        Grid.SetRow(headerSeparator, 1);
        Grid.SetRow(webView, 2);
        Grid.SetRow(footerSeparator, 3);
        Grid.SetRow(footerGrid, 4);

        grid.Children.Add(titleLabel);
        grid.Children.Add(headerSeparator);
        grid.Children.Add(webView);
        grid.Children.Add(footerSeparator);
        grid.Children.Add(footerGrid);

        Content = grid;

        // Apply initial button state after native views are created.
        // Back button starts visible so macOS AppKit creates proper native view,
        // then we hide it on step 1.
        Dispatcher.Dispatch(() =>
        {
            _backButton.IsVisible = false;
        });
    }

    private void OnWizardStateChanged()
    {
        void Apply()
        {
            _isOnLastStep = _bridge.ShowSubmit;
            if (_backButton != null) _backButton.IsVisible = _bridge.ShowBack;
            if (_primaryButton != null)
            {
                _primaryButton.IsEnabled = _bridge.CanProceed && !_bridge.IsSubmitting;
                _primaryButton.Text = _isOnLastStep
                    ? (_bridge.SubmitText ?? DefaultSubmitText)
                    : "Next →";
            }
            if (_submittingIndicator != null)
            {
                _submittingIndicator.IsRunning = _bridge.IsSubmitting;
                _submittingIndicator.IsVisible = _bridge.IsSubmitting;
            }
        }

        if (Dispatcher.IsDispatchRequired)
            Dispatcher.Dispatch(Apply);
        else
            Apply();
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await _bridge.RequestBackAsync();
    }

    private async void OnPrimaryClicked(object? sender, EventArgs e)
    {
        if (_isOnLastStep)
        {
            try
            {
                await _bridge.RequestSubmitAsync();
                if (_bridge.PreventSubmitClose) return;
                var result = (TResult?)_bridge.Result;
                _bridgeHolder.Pop();
                _tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", ex.Message, "OK");
            }
        }
        else
        {
            await _bridge.RequestNextAsync();
        }
    }

    private void OnCancelClicked(object? sender, EventArgs e)
    {
        _bridge.RequestCancel();
        if (_bridge.PreventClose) return;
        _bridgeHolder.Pop();
        _tcs.TrySetResult(default);
    }

    private void OnCloseRequested()
    {
        Dispatcher.Dispatch(() =>
        {
            var result = (TResult?)_bridge.Result;
            _bridgeHolder.Pop();
            _tcs.TrySetResult(result);
        });
    }

    private void OnBlazorReady(double contentHeight)
    {
        ModalWebViewReadySignal.Ready -= OnBlazorReady;
        Dispatcher.Dispatch(async () =>
        {
            if (_webView != null)
                await _webView.FadeToAsync(1, 200, Easing.CubicIn);
        });
    }
}

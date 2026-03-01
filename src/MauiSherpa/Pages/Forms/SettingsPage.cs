using Microsoft.Maui.Controls;
using MauiSherpa.Core.Interfaces;
#if MACOSAPP
using Microsoft.Maui.Platform.MacOS;
using Microsoft.Maui.Platform.MacOS.Controls;
#else
using Microsoft.AspNetCore.Components.WebView.Maui;
#endif

namespace MauiSherpa.Pages.Forms;

/// <summary>
/// Hybrid modal page for Settings.
/// Native title + footer with Save/Cancel/Reset buttons,
/// BlazorWebView for the full settings content.
/// </summary>
public class SettingsPage : ContentPage
{
    private readonly HybridFormBridge _bridge = new();
    private readonly HybridFormBridgeHolder _bridgeHolder;
    private View? _webView;

    public HybridFormBridge Bridge => _bridge;

    public SettingsPage(HybridFormBridgeHolder bridgeHolder)
    {
        _bridgeHolder = bridgeHolder;

#if MACOSAPP
        MacOSPage.SetModalSheetWidth(this, 800);
        MacOSPage.SetModalSheetHeight(this, 600);
#endif
    }

    private TaskCompletionSource<bool>? _tcs;

    public Task<bool> ShowAsync()
    {
        _tcs = new TaskCompletionSource<bool>();
        return _tcs.Task;
    }

    public void Complete(bool saved)
    {
        _tcs?.TrySetResult(saved);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (Content == null)
            BuildPage();
    }

    private void BuildPage()
    {
        _bridgeHolder.Current = _bridge;
        _bridge.CancelRequested += () => Complete(false);

        // Title
        var titleLabel = new Label
        {
            Text = "Settings",
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            Margin = new Thickness(28, 24, 28, 12),
        };

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
            StartPath = "/modal/settings",
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
            StartPath = "/modal/settings",
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


        // Footer buttons: Reset (left) ... Cancel + Save (right)
        var resetButton = new Button
        {
            Text = "Reset to Defaults",
            FontSize = 13,
            BackgroundColor = Colors.Transparent,
            BorderWidth = 0,
            CornerRadius = 5,
            Padding = new Thickness(14, 4),
            HeightRequest = 30,
        };
        resetButton.SetDynamicResource(Button.TextColorProperty, FormTheme.TextMuted);
        resetButton.Clicked += async (_, _) => await _bridge.RequestActionAsync("reset");

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
        cancelButton.Clicked += (_, _) =>
        {
            _bridge.RequestCancel();
        };

        var saveButton = new Button
        {
            Text = "Save",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            CornerRadius = 5,
            Padding = new Thickness(14, 4),
            HeightRequest = 30,
        };
        saveButton.SetDynamicResource(Button.BackgroundColorProperty, FormTheme.AccentPrimary);
        saveButton.Clicked += async (_, _) => await _bridge.RequestActionAsync("save");

        var footerLayout = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
            },
            Margin = new Thickness(28, 12, 28, 24),
            ColumnSpacing = 12,
        };
        Grid.SetColumn(resetButton, 0);
        Grid.SetColumn(cancelButton, 2);
        Grid.SetColumn(saveButton, 3);
        footerLayout.Children.Add(resetButton);
        footerLayout.Children.Add(cancelButton);
        footerLayout.Children.Add(saveButton);

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
        };

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

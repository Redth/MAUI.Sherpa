using Microsoft.Maui.Controls;
#if MACOSAPP
using Microsoft.Maui.Platform.MacOS;
using Microsoft.Maui.Platform.MacOS.Controls;
#else
using Microsoft.AspNetCore.Components.WebView.Maui;
#endif
#if LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Forms;

/// <summary>
/// Base class for hybrid view-only modal pages (no submit, just Close).
/// Uses native MAUI title and Close button with a BlazorWebView for content.
/// Used for read-only or live-update dialogs like capabilities, signatures.
/// </summary>
public abstract class HybridViewPage : ContentPage
{
    private readonly TaskCompletionSource _tcs = new();
    private View? _webView;

    protected abstract string FormTitle { get; }
    protected abstract string BlazorRoute { get; }

    /// <summary>Optional parameters passed to the Blazor component via ModalParameterService.</summary>
    public Dictionary<string, object?> Parameters { get; } = new();

    public HybridViewPage()
    {
        this.SetDynamicResource(BackgroundColorProperty, FormTheme.PageBg);
#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, false);
        MacOSPage.SetModalSheetMinWidth(this, 500);
#elif LINUXGTK
        GtkPage.SetModalSizesToContent(this, false);
        GtkPage.SetModalMinWidth(this, 500);
#endif
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (Content == null)
            BuildPage();
    }

    public Task WaitForCloseAsync() => _tcs.Task;

    private void BuildPage()
    {
        var titleLabel = new Label
        {
            Text = FormTitle,
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            Margin = new Thickness(28, 24, 28, 12),
        };
        titleLabel.SetDynamicResource(Label.TextColorProperty, FormTheme.TextPrimary);

        var headerSeparator = new BoxView { HeightRequest = 1 };
        headerSeparator.SetDynamicResource(BoxView.ColorProperty, FormTheme.Separator);

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

        var footerSeparator = new BoxView { HeightRequest = 1 };
        footerSeparator.SetDynamicResource(BoxView.ColorProperty, FormTheme.Separator);

        var closeButton = new Button
        {
            Text = "Close",
            FontSize = 13,
            BackgroundColor = Colors.Transparent,
            BorderWidth = 0,
            CornerRadius = 5,
            Padding = new Thickness(14, 4),
            HeightRequest = 30,
        };
        closeButton.SetDynamicResource(Button.TextColorProperty, FormTheme.AccentPrimary);
        closeButton.Clicked += OnCloseClicked;

        var footerLayout = new HorizontalStackLayout
        {
            Spacing = 12,
            HorizontalOptions = LayoutOptions.End,
            Margin = new Thickness(28, 12, 28, 24),
            Children = { closeButton },
        };

        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
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

    private void OnCloseClicked(object? sender, EventArgs e)
    {
        _tcs.TrySetResult();
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

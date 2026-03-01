using Microsoft.Maui.Controls;
#if MACOSAPP
using Microsoft.Maui.Platform.MacOS;
using Microsoft.Maui.Platform.MacOS.Controls;
#else
using Microsoft.AspNetCore.Components.WebView.Maui;
#endif

namespace MauiSherpa.Pages.Forms;

/// <summary>
/// Hybrid modal page for progress/operation dialogs.
/// Native title + header separator, BlazorWebView for content,
/// native footer separator + dynamic buttons controlled via ProgressBridge.
/// </summary>
public class HybridProgressPage : ContentPage
{
    private readonly ProgressBridge _bridge;
    private readonly ProgressBridgeHolder _bridgeHolder;
    private readonly string _route;
    private readonly string _initialTitle;
    private View? _webView;
    private Label? _titleLabel;
    private readonly Button[] _footerButtons = new Button[4];
    private HorizontalStackLayout? _footerLayout;
    private bool _built;

    public ProgressBridge Bridge => _bridge;

    public HybridProgressPage(
        ProgressBridgeHolder bridgeHolder,
        string route,
        string title,
        int width = 600,
        int height = 500)
    {
        _bridgeHolder = bridgeHolder;
        _bridge = new ProgressBridge();
        _route = route;
        _initialTitle = title;


#if MACOSAPP
        MacOSPage.SetModalSheetWidth(this, width);
        MacOSPage.SetModalSheetHeight(this, height);
#endif
        BuildPage();
    }

    private void BuildPage()
    {
        if (_built) return;
        _built = true;

        _bridgeHolder.Current = _bridge;

        _bridge.ButtonsChanged += OnButtonsChanged;
        _bridge.TitleChanged += OnTitleChanged;
        _bridge.CloseRequested += OnCloseRequested;

        // Title
        _titleLabel = new Label
        {
            Text = _initialTitle,
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
            StartPath = _route,
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
            StartPath = _route,
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


        // Footer — pre-create button slots. Hidden initially via IsVisible=false.
        // RebuildFooterButtons shows/hides and updates text/style dynamically.
        _footerLayout = new HorizontalStackLayout
        {
            Spacing = 12,
            HorizontalOptions = LayoutOptions.End,
            Margin = new Thickness(28, 12, 28, 24),
        };
        for (int i = 0; i < _footerButtons.Length; i++)
        {
            var idx = i;
            var btn = new Button
            {
                Text = " ",
                FontSize = 13,
                CornerRadius = 5,
                Padding = new Thickness(14, 4),
                HeightRequest = 30,
                IsVisible = false,
            };
            btn.Clicked += async (_, _) => await OnFooterButtonClicked(idx);
            _footerButtons[i] = btn;
            _footerLayout.Children.Add(btn);
        }

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

        Grid.SetRow(_titleLabel, 0);
        Grid.SetRow(headerSeparator, 1);
        Grid.SetRow(webView, 2);
        Grid.SetRow(footerSeparator, 3);
        Grid.SetRow(_footerLayout, 4);

        grid.Children.Add(_titleLabel);
        grid.Children.Add(headerSeparator);
        grid.Children.Add(webView);
        grid.Children.Add(footerSeparator);
        grid.Children.Add(_footerLayout);

        Content = grid;
    }

    private void OnButtonsChanged()
    {
        Dispatcher.Dispatch(() => RebuildFooterButtons());
    }

    private void OnTitleChanged()
    {
        Dispatcher.Dispatch(() =>
        {
            if (_titleLabel != null && _bridge.Title != null)
                _titleLabel.Text = _bridge.Title;
        });
    }

    private void OnCloseRequested()
    {
        // The service handles popping the modal — just signal completion
    }

    private string[] _buttonIds = Array.Empty<string>();

    private async Task OnFooterButtonClicked(int index)
    {
        if (index < _buttonIds.Length)
            await _bridge.OnButtonClickedAsync(_buttonIds[index]);
    }

    private void RebuildFooterButtons()
    {
        var buttons = _bridge.Buttons;
        _buttonIds = buttons.Select(b => b.Id).ToArray();

        for (int i = 0; i < _footerButtons.Length; i++)
        {
            var btn = _footerButtons[i];
            if (i < buttons.Count)
            {
                var def = buttons[i];
                btn.Text = def.Text;
                btn.IsEnabled = def.Enabled;

                // Reset styles
                btn.FontAttributes = FontAttributes.None;
                btn.BorderWidth = 0;
                btn.ImageSource = null;
                btn.ClearValue(Button.TextColorProperty);
                btn.ClearValue(Button.BackgroundColorProperty);
                btn.ClearValue(Button.BackgroundProperty);

                switch (def.Style)
                {
                    case ProgressButtonStyle.Primary:
                        btn.FontAttributes = FontAttributes.Bold;
                        btn.TextColor = Colors.White;
                        btn.SetDynamicResource(Button.BackgroundColorProperty, FormTheme.AccentPrimary);
                        break;
                    case ProgressButtonStyle.Warning:
                        btn.FontAttributes = FontAttributes.Bold;
                        btn.TextColor = Colors.White;
                        btn.BackgroundColor = Color.FromArgb("#ed8936");
                        break;
                    case ProgressButtonStyle.Danger:
                        btn.FontAttributes = FontAttributes.Bold;
                        btn.TextColor = Colors.White;
                        btn.BackgroundColor = Color.FromArgb("#e53e3e");
                        break;
                    case ProgressButtonStyle.Copilot:
                        btn.FontAttributes = FontAttributes.Bold;
                        btn.TextColor = Colors.White;
                        // NSButton on macOS doesn't render LinearGradientBrush — use solid indigo
                        btn.BackgroundColor = Color.FromArgb("#4f46e5");
                        break;
                    default: // Secondary
                        btn.BackgroundColor = Colors.Transparent;
                        btn.SetDynamicResource(Button.TextColorProperty, FormTheme.AccentPrimary);
                        break;
                }

                btn.IsVisible = true;
            }
            else
            {
                btn.IsVisible = false;
            }
        }
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

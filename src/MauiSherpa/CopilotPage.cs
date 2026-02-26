using Microsoft.Maui.Controls;
using MauiSherpa.Core.Interfaces;
#if MACOSAPP
using Microsoft.Maui.Platform.MacOS.Controls;
#else
using Microsoft.AspNetCore.Components.WebView.Maui;
#endif

namespace MauiSherpa;

/// <summary>
/// A ContentPage hosting a dedicated BlazorWebView for the Copilot chat.
/// The page and its WebView are kept alive across show/hide cycles.
/// Presented modally via Navigation.PushModalAsync/PopModalAsync.
/// 
/// Layout: Native header bar + BlazorWebView (chat history) + native input bar.
/// On macOS AppKit, uses MacOSBlazorWebView. On Mac Catalyst/Windows, uses
/// the standard BlazorWebView with HandlerDisconnectPolicy.Manual.
/// </summary>
public class CopilotPage : ContentPage
{
    private static readonly Color HeaderBg = Color.FromArgb("#6366f1");
    private static readonly Color HeaderBgDark = Color.FromArgb("#4f46e5");
    private static readonly Color AccentSuccess = Color.FromArgb("#48bb78");
    private static readonly Color AccentDanger = Color.FromArgb("#f56565");
    private static readonly Color SeparatorColor = Color.FromArgb("#e2e8f0");

    private readonly ICopilotContextService _contextService;
    private readonly ICopilotService _copilotService;
    private readonly Editor _inputEditor;
    private readonly Button _sendButton;
    private readonly Label _statusLabel;
    private readonly ActivityIndicator _busyIndicator;

    // Header elements (assigned in BuildHeaderBar, called from constructor)
    private BoxView _statusDot = null!;
    private Label _headerStatusLabel = null!;
    private Label _headerLoginBadge = null!;
    private Label _headerVersionBadge = null!;
    private Button _connectButton = null!;
    private Button _disconnectButton = null!;
    private ActivityIndicator _connectingIndicator = null!;

    public CopilotPage(
        ICopilotContextService contextService,
        ICopilotService copilotService,
        IServiceProvider serviceProvider)
    {
        _contextService = contextService;
        _copilotService = copilotService;

        // --- BlazorWebView for chat history (platform-specific) ---
        View webView;
#if MACOSAPP
        var blazorWebView = new MacOSBlazorWebView
        {
            HostPage = "wwwroot/index.html",
            StartPath = "/copilot-modal",
        };
        blazorWebView.RootComponents.Add(new BlazorRootComponent
        {
            Selector = "#app",
            ComponentType = typeof(Components.CopilotModalApp)
        });
        webView = blazorWebView;
#else
        var blazorWebView = new BlazorWebView
        {
            HostPage = "wwwroot/index.html",
            StartPath = "/copilot-modal",
        };
        blazorWebView.RootComponents.Add(new RootComponent
        {
            Selector = "#app",
            ComponentType = typeof(Components.CopilotModalApp)
        });
        HandlerProperties.SetDisconnectPolicy(blazorWebView, HandlerDisconnectPolicy.Manual);
        webView = blazorWebView;
#endif

        // --- Native header bar ---
        var headerBar = BuildHeaderBar(serviceProvider);

        // --- Native input bar ---
        _statusLabel = new Label { IsVisible = false }; // kept for API compat, header shows status

        _busyIndicator = new ActivityIndicator
        {
            IsRunning = false,
            IsVisible = false,
            Color = Color.FromArgb("#8b5cf6"),
            WidthRequest = 16,
            HeightRequest = 16,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(4, 0, 0, 0),
        };

        _inputEditor = new Editor
        {
            Placeholder = "Ask Copilot something...",
            AutoSize = EditorAutoSizeOption.TextChanges,
            MaximumHeightRequest = 120,
            FontSize = 14,
            VerticalOptions = LayoutOptions.Center,
        };

        _sendButton = new Button
        {
            Text = "\u2191", // ↑ arrow up
            FontSize = 20,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#6366f1"),
            CornerRadius = 20,
            WidthRequest = 40,
            HeightRequest = 40,
            Padding = 0,
            VerticalOptions = LayoutOptions.End,
            Margin = new Thickness(0, 0, 0, 4),
        };
        _sendButton.Clicked += OnSendClicked;
        _inputEditor.Completed += OnEditorCompleted;

        // Input row: editor + busy indicator + send button
        var inputRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
            },
            Padding = new Thickness(12, 8, 12, 8),
            ColumnSpacing = 8,
        };
        inputRow.Add(_inputEditor, 0, 0);
        inputRow.Add(_busyIndicator, 1, 0);
        inputRow.Add(_sendButton, 2, 0);

        var separator = new BoxView
        {
            HeightRequest = 1,
            Color = SeparatorColor,
        };

        // --- Page layout ---
#if LINUXGTK
        // On Linux GTK, the HeaderBar takes space that MAUI layout doesn't account for,
        // causing the bottom input bar to be clipped. Use only the BlazorWebView
        // (which knows its real GTK-allocated size) and let the Blazor content provide
        // its own input area via HideInputArea=false on the embedded Copilot component.
        var layout = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),   // webview fills all space
            },
        };
        layout.Add(webView, 0, 0);
#else
        var layout = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),   // header
                new RowDefinition(GridLength.Star),   // webview
                new RowDefinition(GridLength.Auto),   // separator
                new RowDefinition(GridLength.Auto),   // input bar
            },
        };
        layout.Add(separator, 0, 2);
        layout.Add(inputRow, 0, 3);

#if MACOSAPP
        // On macOS, WKWebView reserves a ~52px top inset for the window titlebar,
        // creating a visible gap between the header and web content. Work around this
        // by pulling the WebView up behind the opaque header.
        // Add WebView BEFORE header so the header's native NSView is on top for
        // AppKit hit-testing (subviews added later receive clicks first).
        layout.Add(webView, 0, 1);
        layout.Add(headerBar, 0, 0);
        webView.Margin = new Thickness(0, -52, 0, 0);
        headerBar.ZIndex = 10;
#else
        layout.Add(headerBar, 0, 0);
        layout.Add(webView, 0, 1);
#endif
#endif

        Content = layout;

#if MACCATALYST
        Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.Page.SetUseSafeArea(this, false);
#endif

        _contextService.OnBusyStateChanged += OnBusyStateChanged;
        _contextService.OnConnectionStateChanged += OnConnectionStateChanged;
        UpdateConnectionStatus();
    }

    private View BuildHeaderBar(IServiceProvider serviceProvider)
    {
        // Status dot (green/red circle)
        _statusDot = new BoxView
        {
            WidthRequest = 10,
            HeightRequest = 10,
            CornerRadius = 5,
            Color = AccentDanger,
            VerticalOptions = LayoutOptions.Center,
        };

        // Copilot icon
        var copilotIcon = new Image
        {
#if MACOSAPP
            Source = ImageSource.FromFile("ghcp_icon_white.png"),
#else
            Source = "ghcp_icon_white",
#endif
            WidthRequest = 20,
            HeightRequest = 20,
            VerticalOptions = LayoutOptions.Center,
        };

        // Title
        var titleLabel = new Label
        {
            Text = "Copilot",
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            VerticalOptions = LayoutOptions.Center,
        };

        // Status text (Connected / Disconnected)
        _headerStatusLabel = new Label
        {
            Text = "Disconnected",
            FontSize = 12,
            TextColor = Color.FromArgb("#e0e0ff"),
            VerticalOptions = LayoutOptions.Center,
        };

        // Login badge (@Username)
        _headerLoginBadge = new Label
        {
            FontSize = 11,
            TextColor = Colors.White,
            BackgroundColor = new Color(0.49f, 0.43f, 0.94f), // purple, same as header but lighter
            Padding = new Thickness(6, 2),
            VerticalOptions = LayoutOptions.Center,
            IsVisible = false,
        };
#if MACOSAPP
        // macOS Label doesn't support CornerRadius natively, skip
#endif

        // Version badge
        _headerVersionBadge = new Label
        {
            FontSize = 11,
            TextColor = Color.FromArgb("#c0c0ff"),
            VerticalOptions = LayoutOptions.Center,
            IsVisible = false,
        };

        // Connect button
        _connectButton = new Button
        {
            Text = "Connect",
            FontSize = 12,
            TextColor = HeaderBg,
            BackgroundColor = Colors.White,
            CornerRadius = 4,
            Padding = new Thickness(12, 4),
            HeightRequest = 28,
            VerticalOptions = LayoutOptions.Center,
            IsVisible = false,
        };
        _connectButton.Clicked += async (_, _) => await ConnectAsync();

        _connectingIndicator = new ActivityIndicator
        {
            IsRunning = false,
            IsVisible = false,
            Color = Colors.White,
            WidthRequest = 16,
            HeightRequest = 16,
            VerticalOptions = LayoutOptions.Center,
        };

        // Disconnect button
        _disconnectButton = new Button
        {
            Text = "Disconnect",
            FontSize = 12,
            TextColor = Colors.White,
            BackgroundColor = new Color(1f, 1f, 1f, 0.125f), // white with 12.5% opacity
            CornerRadius = 4,
            Padding = new Thickness(12, 4),
            HeightRequest = 28,
            VerticalOptions = LayoutOptions.Center,
            IsVisible = false,
        };
        _disconnectButton.Clicked += async (_, _) => await DisconnectAsync();

        // Close button (X)
        var closeButton = new Button
        {
            Text = "\u2715", // ✕
            FontSize = 16,
            TextColor = Colors.White,
            BackgroundColor = Colors.Transparent,
            Padding = new Thickness(8, 2),
            HeightRequest = 32,
            WidthRequest = 32,
            VerticalOptions = LayoutOptions.Center,
        };
        closeButton.Clicked += async (_, _) =>
        {
            var modalService = serviceProvider.GetService<ICopilotModalService>();
            if (modalService != null)
                await modalService.CloseAsync();
        };

        // Left side: icon + title + status
        var leftGroup = new HorizontalStackLayout
        {
            Spacing = 8,
            VerticalOptions = LayoutOptions.Center,
            Children = { copilotIcon, titleLabel, _statusDot, _headerStatusLabel, _headerLoginBadge, _headerVersionBadge },
        };

        // Right side: buttons
        var rightGroup = new HorizontalStackLayout
        {
            Spacing = 6,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End,
            Children = { _connectingIndicator, _connectButton, _disconnectButton, closeButton },
        };

        var headerGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            BackgroundColor = HeaderBg,
            Padding = new Thickness(16, 10),
        };
        headerGrid.Add(leftGroup, 0, 0);
        headerGrid.Add(rightGroup, 1, 0);

        return headerGrid;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        UpdateConnectionStatus();
        _inputEditor.Focus();
    }

    private void OnSendClicked(object? sender, EventArgs e) => SubmitInput();

    private void OnEditorCompleted(object? sender, EventArgs e) => SubmitInput();

    private void SubmitInput()
    {
        var text = _inputEditor.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;
        if (_contextService.IsChatBusy) return;

        _inputEditor.Text = "";
        _contextService.SubmitMessage(text);
    }

    private void OnBusyStateChanged(bool isBusy)
    {
        Dispatcher.Dispatch(() =>
        {
            _busyIndicator.IsRunning = isBusy;
            _busyIndicator.IsVisible = isBusy;
            _sendButton.IsEnabled = !isBusy;
            _inputEditor.IsEnabled = !isBusy;
            UpdateConnectionStatus();
        });
    }

    private void OnConnectionStateChanged()
    {
        Dispatcher.Dispatch(UpdateConnectionStatus);
    }

    private async Task ConnectAsync()
    {
        _connectingIndicator.IsRunning = true;
        _connectingIndicator.IsVisible = true;
        _connectButton.IsVisible = false;
        try
        {
            await _copilotService.ConnectAsync();
        }
        catch { /* handled by service */ }
        finally
        {
            Dispatcher.Dispatch(() =>
            {
                _connectingIndicator.IsRunning = false;
                _connectingIndicator.IsVisible = false;
                UpdateConnectionStatus();
            });
        }
    }

    private async Task DisconnectAsync()
    {
        try
        {
            await _copilotService.DisconnectAsync();
        }
        catch { /* handled by service */ }
        finally
        {
            Dispatcher.Dispatch(UpdateConnectionStatus);
        }
    }

    private void UpdateConnectionStatus()
    {
        var connected = _copilotService.IsConnected;

        // Header
        _statusDot.Color = connected ? AccentSuccess : AccentDanger;
        _headerStatusLabel.Text = connected ? "Connected" : "Disconnected";
        _connectButton.IsVisible = !connected;
        _disconnectButton.IsVisible = connected;

        // Login/version badges
        var availability = _copilotService.CachedAvailability;
        if (availability?.Login != null)
        {
            _headerLoginBadge.Text = $"@{availability.Login}";
            _headerLoginBadge.IsVisible = true;
        }
        else
        {
            _headerLoginBadge.IsVisible = false;
        }

        if (availability?.Version != null)
        {
            _headerVersionBadge.Text = $"v{availability.Version}";
            _headerVersionBadge.IsVisible = true;
        }
        else
        {
            _headerVersionBadge.IsVisible = false;
        }

        // Input bar
        _inputEditor.IsEnabled = true;
        _sendButton.IsEnabled = true;
    }
}

using Microsoft.AspNetCore.Components.WebView.Maui;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa;

public class MainPage : ContentPage
{
    private readonly Grid _splashOverlay;
    private readonly ISplashService _splashService;
    
    private readonly BlazorWebView _blazorWebView;

    public MainPage(ISplashService splashService)
    {
        _splashService = splashService;
        
        _blazorWebView = new BlazorWebView
        {
            HostPage = "wwwroot/index.html"
        };
        _blazorWebView.RootComponents.Add(new RootComponent
        {
            Selector = "#app",
            ComponentType = typeof(Components.App)
        });

        // Create splash overlay
        _splashOverlay = CreateSplashOverlay();
        
        // Use a Grid to layer the BlazorWebView and splash
        var container = new Grid();
        container.Children.Add(_blazorWebView);
        container.Children.Add(_splashOverlay);
        
        Content = container;

#if MACCATALYST
        // Extend content into the hidden titlebar area  
        Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.Page.SetUseSafeArea(this, false);
#endif
        
        // Subscribe to Blazor ready event
        _splashService.OnBlazorReady += OnBlazorReady;
        
        // Safety timeout - hide splash after 15 seconds
        Dispatcher.StartTimer(TimeSpan.FromSeconds(15), () =>
        {
            if (_splashOverlay.Opacity > 0)
            {
                HideSplash();
            }
            return false; // Don't repeat
        });
    }
    
    private Grid CreateSplashOverlay()
    {
        var overlay = new Grid
        {
            BackgroundColor = Color.FromArgb("#1a1625"),
            ZIndex = 1000
        };
        
        // Add gradient background using BoxView layers
        var gradientTop = new BoxView
        {
            Color = Color.FromArgb("#2d1f4e"),
            Opacity = 0.5
        };
        overlay.Children.Add(gradientTop);
        
        // Content stack
        var contentStack = new VerticalStackLayout
        {
            Spacing = 0,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        
        // App logo image
        var logoImage = new Image
        {
            Source = "sherpalogo.png",
            WidthRequest = 200,
            HeightRequest = 200,
            HorizontalOptions = LayoutOptions.Center
        };
        contentStack.Children.Add(logoImage);
        
        // Title
        var title = new Label
        {
            Text = "MAUI Sherpa",
            TextColor = Colors.White,
            FontSize = 28,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 24, 0, 0)
        };
        contentStack.Children.Add(title);
        
        // Subtitle
        var subtitle = new Label
        {
            Text = "Your guide to .NET MAUI development",
            TextColor = Color.FromArgb("#9999aa"),
            FontSize = 14,
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 8, 0, 0)
        };
        contentStack.Children.Add(subtitle);
        
        // Loading indicator
        var loadingIndicator = new ActivityIndicator
        {
            IsRunning = true,
            Color = Color.FromArgb("#8b5cf6"),
            WidthRequest = 32,
            HeightRequest = 32,
            Margin = new Thickness(0, 40, 0, 0),
            HorizontalOptions = LayoutOptions.Center
        };
        contentStack.Children.Add(loadingIndicator);
        
        overlay.Children.Add(contentStack);
        
        return overlay;
    }
    
    private void OnBlazorReady()
    {
        Dispatcher.Dispatch(() => HideSplash());
    }
    
    private async void HideSplash()
    {
        // Fade out animation
        await _splashOverlay.FadeToAsync(0, 400, Easing.CubicOut);
        _splashOverlay.IsVisible = false;
        // Remove from tree so it can't intercept input (WinUI hidden views can block scroll)
        ((Grid)Content).Children.Remove(_splashOverlay);
        
        // Focus the BlazorWebView so it receives trackpad/scroll input on Windows
        _blazorWebView.Focus();
    }
    
    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Ensure WebView has focus for trackpad/scroll input on Windows
        _blazorWebView.Focus();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _splashService.OnBlazorReady -= OnBlazorReady;
    }
}

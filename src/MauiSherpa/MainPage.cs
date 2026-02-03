using Microsoft.AspNetCore.Components.WebView.Maui;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa;

public class MainPage : ContentPage
{
    private readonly Grid _splashOverlay;
    private readonly ISplashService _splashService;
    
    public MainPage(ISplashService splashService)
    {
        _splashService = splashService;
        
        var blazorWebView = new BlazorWebView
        {
            HostPage = "wwwroot/index.html"
        };
        blazorWebView.RootComponents.Add(new RootComponent
        {
            Selector = "#app",
            ComponentType = typeof(Components.App)
        });

        // Create splash overlay
        _splashOverlay = CreateSplashOverlay();
        
        // Use a Grid to layer the BlazorWebView and splash
        var container = new Grid();
        container.Children.Add(blazorWebView);
        container.Children.Add(_splashOverlay);
        
        Content = container;
        
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
    }
    
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _splashService.OnBlazorReady -= OnBlazorReady;
    }
}

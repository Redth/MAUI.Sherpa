using MauiSherpa.Pages.Forms;

namespace MauiSherpa;

public sealed class HybridLoadingView : Grid
{
    private readonly ThemedBlazorWebView _webView;
    private readonly ActivityIndicator _indicator;
    private bool _ready;
    private bool _stopped;

    public event Action<double>? ContentReady;

    public HybridLoadingView(ThemedBlazorWebView webView)
    {
        _webView = webView;
        _webView.Opacity = 0;
        SetBinding(BackgroundColorProperty, new Binding(nameof(BackgroundColor), source: webView));

        _indicator = new ActivityIndicator
        {
            AutomationId = "HybridLoadingIndicator",
            IsRunning = true,
            WidthRequest = 32,
            HeightRequest = 32,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true,
        };
        _indicator.SetDynamicResource(ActivityIndicator.ColorProperty, FormTheme.AccentPrimary);
        SemanticProperties.SetDescription(_indicator, "Loading content");

        var root = webView.RootComponents.Single();
        var contentType = root.ComponentType;
        var contentParameters = root.Parameters;
        root.ComponentType = typeof(Components.HybridLoadingRoot);
        root.Parameters = new Dictionary<string, object?>
        {
            [nameof(Components.HybridLoadingRoot.ContentType)] = contentType,
            [nameof(Components.HybridLoadingRoot.ContentParameters)] = contentParameters,
            [nameof(Components.HybridLoadingRoot.ContentReady)] = new Func<double, Task>(OnContentReadyAsync),
        };

        Children.Add(webView);
        Children.Add(_indicator);
    }

    public void StopLoading()
    {
        if (Dispatcher.IsDispatchRequired)
            Dispatcher.Dispatch(StopLoadingCore);
        else
            StopLoadingCore();
    }

    private void StopLoadingCore()
    {
        _stopped = true;
        _webView.CancelAnimations();
        _indicator.IsRunning = false;
        _indicator.IsVisible = false;
    }

    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
        if (args.OldHandler != null && args.NewHandler == null)
            _indicator.IsRunning = false;
        base.OnHandlerChanging(args);
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (Handler != null && !_ready && !_stopped)
            _indicator.IsRunning = true;
    }

    private Task OnContentReadyAsync(double contentHeight) => Dispatcher.DispatchAsync(async () =>
    {
        if (_ready || _stopped)
            return;

        _ready = true;
        ContentReady?.Invoke(contentHeight);
        if (Handler != null && _webView.Handler != null)
            await _webView.FadeToAsync(1, 200, Easing.CubicIn);
        else
            _webView.Opacity = 1;

        _indicator.IsRunning = false;
        _indicator.IsVisible = false;
    });
}

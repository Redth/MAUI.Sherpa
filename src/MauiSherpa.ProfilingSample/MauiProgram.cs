using Microsoft.Extensions.Logging;
using MauiSherpa.ProfilingSample.Services;
#if DEBUG
using MauiDevFlow.Agent;
using MauiDevFlow.Blazor;
#endif

namespace MauiSherpa.ProfilingSample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddSingleton<ProfilingScenarioService>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
        builder.AddMauiDevFlowAgent(options =>
        {
            options.EnableProfiler = true;
            options.EnableHighLevelUiHooks = true;
            options.EnableDetailedUiHooks = true;
        });
        builder.AddMauiBlazorDevFlowTools();
#endif

        return builder.Build();
    }
}

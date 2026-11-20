namespace MauiSherpa.Core.Services;

public static class AppClock
{
    public static DateTime UtcNow => Screenshots.ScreenshotMode.Current.IsEnabled
        ? Screenshots.ScreenshotMode.ReferenceDate : DateTime.UtcNow;
}

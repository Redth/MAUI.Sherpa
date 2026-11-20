using System.Collections.Concurrent;
using MauiSherpa.Core.Screenshots;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MauiSherpa.Services;

public static class ScreenshotPlatformServices
{
    public static void AddScreenshotPlatformServices(this IServiceCollection services)
    {
        if (!ScreenshotMode.Current.IsEnabled)
            return;
        services.RemoveAll<IPreferences>();
        services.RemoveAll<ISecureStorage>();
        services.RemoveAll<ILauncher>();
        services.RemoveAll<IClipboard>();
        services.AddSingleton<ScreenshotPlatformState>();
        services.AddSingleton<IPreferences>(sp => sp.GetRequiredService<ScreenshotPlatformState>());
        services.AddSingleton<ISecureStorage>(sp => sp.GetRequiredService<ScreenshotPlatformState>());
        services.AddSingleton<ILauncher>(sp => sp.GetRequiredService<ScreenshotPlatformState>());
        services.AddSingleton<IClipboard>(sp => sp.GetRequiredService<ScreenshotPlatformState>());
    }

    private sealed class ScreenshotPlatformState : IPreferences, ISecureStorage, ILauncher, IClipboard
    {
        private readonly ConcurrentDictionary<(string?, string), object> preferences = new();
        private readonly ConcurrentDictionary<string, string> secrets = new();
        private string? clipboard;

        public ScreenshotPlatformState()
        {
            Set("window_width", (double)ScreenshotMode.WindowWidth);
            Set("window_height", (double)ScreenshotMode.WindowHeight);
            Set("sidebar_width", (double)ScreenshotMode.SidebarWidth);
        }

        public bool ContainsKey(string key, string? sharedName = null) => preferences.ContainsKey((sharedName, key));
        public void Remove(string key, string? sharedName = null) => preferences.TryRemove((sharedName, key), out _);
        public void Clear(string? sharedName = null)
        {
            foreach (var key in preferences.Keys.Where(k => k.Item1 == sharedName))
                preferences.TryRemove(key, out _);
        }
        public T Get<T>(string key, T defaultValue, string? sharedName = null) =>
            preferences.TryGetValue((sharedName, key), out var value) && value is T typed ? typed : defaultValue;
        public void Set<T>(string key, T value, string? sharedName = null)
        {
            if (value is null)
                Remove(key, sharedName);
            else
                preferences[(sharedName, key)] = value;
        }
        public Task<string?> GetAsync(string key) => Task.FromResult(secrets.GetValueOrDefault(key));
        public Task SetAsync(string key, string value) { secrets[key] = value; return Task.CompletedTask; }
        public bool Remove(string key) => secrets.TryRemove(key, out _);
        public void RemoveAll() => secrets.Clear();
        private static Task<bool> BlockLaunch()
        {
            ScreenshotMode.BlockExternalAction("external launch");
            throw new InvalidOperationException("Screenshot platform services require Screenshot Mode.");
        }
        public Task<bool> CanOpenAsync(Uri uri) => Task.FromResult(false);
        public Task<bool> CanOpenAsync(string uri) => Task.FromResult(false);
        public Task<bool> OpenAsync(Uri uri) => BlockLaunch();
        public Task<bool> OpenAsync(string uri) => BlockLaunch();
        public Task<bool> OpenAsync(OpenFileRequest request) => BlockLaunch();
        public Task<bool> TryOpenAsync(Uri uri) => BlockLaunch();
        public bool HasText => !string.IsNullOrEmpty(clipboard);
        public Task<string?> GetTextAsync() => Task.FromResult(clipboard);
        public event EventHandler<EventArgs>? ClipboardContentChanged;
        public Task SetTextAsync(string? text)
        {
            clipboard = text;
            ClipboardContentChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
    }
}

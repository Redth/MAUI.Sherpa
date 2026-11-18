namespace MauiSherpa.Services;

internal static class NativeUiDispatcher
{
    public static Task RunAsync(Func<Task> action)
    {
#if LINUXGTK
        var dispatcher = Application.Current?.Dispatcher
            ?? throw new InvalidOperationException("The application dispatcher is unavailable.");
        return dispatcher.DispatchAsync(action);
#else
        return action();
#endif
    }
}

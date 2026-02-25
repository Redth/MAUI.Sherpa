using AppKit;

namespace MauiSherpa;

public static class MainClass
{
    static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var msg = $"[UNHANDLED] {e.ExceptionObject}";
            try { File.WriteAllText("/tmp/maui-sherpa-crash.txt", msg); } catch { }
            Console.Error.WriteLine(msg);
            Console.Error.Flush();
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            var msg = $"[UNOBSERVED] {e.Exception}";
            try { File.AppendAllText("/tmp/maui-sherpa-crash.txt", "\n" + msg); } catch { }
        };

        ObjCRuntime.Runtime.MarshalManagedException += (_, mmeArgs) =>
        {
            var msg = $"[MARSHAL-MANAGED] {mmeArgs.Exception}";
            try { File.AppendAllText("/tmp/maui-sherpa-crash.txt", "\n" + msg); } catch { }
            Console.Error.WriteLine(msg);
            Console.Error.Flush();
        };

        NSApplication.Init();
        NSApplication.SharedApplication.Delegate = new MauiMacOSApp();
        NSApplication.Main(args);
    }
}

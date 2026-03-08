using System.CommandLine;

namespace MauiSherpa.Cli.Commands.Android;

public static class AndroidCommand
{
    public static Command Create()
    {
        var cmd = new Command("android", "Android SDK, emulator, device, and keystore management tools.");
        cmd.Add(SdkCommand.Create());
        cmd.Add(EmulatorsCommand.Create());
        cmd.Add(DevicesCommand.Create());
        cmd.Add(KeystoresCommand.Create());
        cmd.Add(LogsCommand.Create());
        return cmd;
    }
}

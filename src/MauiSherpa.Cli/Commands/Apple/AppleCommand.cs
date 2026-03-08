using System.CommandLine;

namespace MauiSherpa.Cli.Commands.Apple;

public static class AppleCommand
{
    public static Command Create()
    {
        var cmd = new Command("apple", "iOS simulator, device, Xcode, and provisioning profile management tools (macOS only).");
        cmd.Add(SimulatorsCommand.Create());
        cmd.Add(DevicesCommand.Create());
        cmd.Add(XcodeCommand.Create());
        cmd.Add(ProfilesCommand.Create());
        cmd.Add(LogsCommand.Create());
        return cmd;
    }
}

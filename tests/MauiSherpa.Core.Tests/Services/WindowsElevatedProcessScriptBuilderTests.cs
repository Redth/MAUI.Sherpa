using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public class WindowsElevatedProcessScriptBuilderTests
{
    [Fact]
    public void BuildPreservesCommandContextAndEscapesPowerShellValues()
    {
        var request = new ProcessRequest(
            Command: @"C:\Program Files\dotnet\dotnet.exe",
            Arguments: ["workload", "install", "maui", "--version", "10.0.300.1"],
            WorkingDirectory: @"C:\Users\Test User\project",
            Environment: new Dictionary<string, string>
            {
                ["DOTNET_ROOT"] = @"C:\Program Files\dotnet",
                ["SPECIAL"] = "it's valid"
            },
            UsePseudoTerminal: true);

        var script = WindowsElevatedProcessScriptBuilder.Build(
            request,
            @"C:\Users\Test User\AppData\Local\Temp\output.log");

        script.Should().Contain("Set-Location -LiteralPath 'C:\\Users\\Test User\\project'");
        script.Should().Contain("Set-Item -LiteralPath 'Env:DOTNET_ROOT' -Value 'C:\\Program Files\\dotnet'");
        script.Should().Contain("Set-Item -LiteralPath 'Env:SPECIAL' -Value 'it''s valid'");
        script.Should().Contain("& 'C:\\Program Files\\dotnet\\dotnet.exe' 'workload' 'install' 'maui'");
        script.Should().Contain("Tee-Object -FilePath 'C:\\Users\\Test User\\AppData\\Local\\Temp\\output.log'");
        script.Should().Contain("__EXIT_CODE__:$exitCode");
    }
}

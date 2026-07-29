using FluentAssertions;
using MauiSherpa.Core.Models.Profiling;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public class MauiCliJsonStreamParserTests
{
    [Fact]
    public void Append_ParsesProfileResultAfterHumanOutput()
    {
        var parser = new MauiCliJsonStreamParser();
        var output = """
            $ maui profile startup
            {
              "project_path": "/repo/App.csproj",
              "project_name": "App",
              "framework": "net10.0-android",
              "platform": "android",
              "device_id": "emulator-5554",
              "device_name": "Pixel 8",
              "configuration": "Release",
              "format": "speedscope",
              "output_path": "/tmp/capture.speedscope.json",
              "raw_trace_path": "/tmp/capture.nettrace",
              "used_stopping_event": true
            }
            """;

        var messages = parser.Append(output);

        var result = messages.Should().ContainSingle()
            .Which.Should().BeOfType<MauiProfileResultMessage>().Subject.Result;
        result.OutputPath.Should().EndWith("capture.speedscope.json");
        result.RawTracePath.Should().EndWith("capture.nettrace");
        result.UsedStoppingEvent.Should().BeTrue();
    }

    [Fact]
    public void Append_ParsesJsonAcrossFragmentsAndFutureProperties()
    {
        var parser = new MauiCliJsonStreamParser();

        parser.Append("{\"status\":\"pro").Should().BeEmpty();
        var messages = parser.Append("gress\",\"message\":\"Building\",\"percentage\":25,\"future\":true}");

        messages.Should().ContainSingle()
            .Which.Should().Be(new MauiCliStatusMessage("progress", "Building", 25));
    }

    [Fact]
    public void Append_ParsesCanonicalErrorEnvelope()
    {
        var parser = new MauiCliJsonStreamParser();
        var output = """
            {
              "code": "E2403",
              "category": "platform",
              "severity": "error",
              "message": "Diagnostics tool not found",
              "remediation": {
                "type": "useraction",
                "manual_steps": ["Install dotnet-trace", "Retry"]
              }
            }
            """;

        var error = parser.Append(output).Should().ContainSingle()
            .Which.Should().BeOfType<MauiCliErrorMessage>().Subject;

        error.Code.Should().Be("E2403");
        error.Remediation!.ManualSteps.Should().HaveCount(2);
    }

    [Fact]
    public void Append_ParsesDeviceList()
    {
        var parser = new MauiCliJsonStreamParser();
        var output = """
            [
              {
                "name": "Pixel 8",
                "identifier": "emulator-5554",
                "platforms": ["android"],
                "version": "35",
                "is_emulator": true,
                "is_running": true
              }
            ]
            """;

        var list = parser.Append(output).Should().ContainSingle()
            .Which.Should().BeOfType<MauiCliDeviceListMessage>().Subject;

        list.Devices.Should().ContainSingle();
        list.Devices[0].Platform.Should().Be("android");
        list.Devices[0].IsRunning.Should().BeTrue();
    }

    [Fact]
    public void Append_IgnoresInvalidBraceDelimitedHumanOutput()
    {
        var parser = new MauiCliJsonStreamParser();

        var messages = parser.Append("Building target {not-json}\n");

        messages.Should().BeEmpty();
    }
}

using System.Text.Json;
using FluentAssertions;
using MauiSherpa.Workloads.Services;

namespace MauiSherpa.Workloads.Tests.Services;

public sealed class GlobalJsonWorkloadPinEditorTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"mauisherpa-globaljson-{Guid.NewGuid():N}");

    public GlobalJsonWorkloadPinEditorTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task AddsOfficialPinWithoutRewritingCommentsOrUnknownProperties()
    {
        var path = Path.Combine(_directory, "global.json");
        var original = """
        {
          // Keep this project note.
          "sdk": {
            "version": "10.0.302",
          },
          "custom": true,
        }
        """;
        await File.WriteAllTextAsync(path, original);
        var editor = new GlobalJsonWorkloadPinEditor();

        var preview = editor.Preview(_directory, "10.0.302");
        await editor.ApplyAsync(preview);
        var updated = await File.ReadAllTextAsync(path);

        updated.Should().Contain("// Keep this project note.");
        updated.Should().Contain("\"custom\": true");
        updated.Should().Contain("\"workloadVersion\": \"10.0.302\"");
        new GlobalJsonService().ParseGlobalJson(path)!.WorkloadSetVersion.Should().Be("10.0.302");
    }

    [Fact]
    public async Task RemovesOnlyOfficialPin()
    {
        var path = Path.Combine(_directory, "global.json");
        await File.WriteAllTextAsync(path, """
        {
          "sdk": {
            "version": "10.0.302",
            "workloadVersion": "10.0.300.1",
            "rollForward": "latestPatch"
          }
        }
        """);
        var editor = new GlobalJsonWorkloadPinEditor();

        await editor.ApplyAsync(editor.Preview(_directory, null));
        var updated = await File.ReadAllTextAsync(path);

        updated.Should().NotContain("workloadVersion");
        using var document = JsonDocument.Parse(updated);
        document.RootElement.GetProperty("sdk").GetProperty("version").GetString().Should().Be("10.0.302");
        document.RootElement.GetProperty("sdk").GetProperty("rollForward").GetString().Should().Be("latestPatch");
    }

    [Fact]
    public async Task CreatesGlobalJsonWhenProjectHasNone()
    {
        var editor = new GlobalJsonWorkloadPinEditor();

        await editor.ApplyAsync(editor.Preview(_directory, "11.0.100-preview.6.26359.118"));

        new GlobalJsonService().ParseGlobalJson(Path.Combine(_directory, "global.json"))!
            .WorkloadSetVersion.Should().Be("11.0.100-preview.6.26359.118");
    }

    [Fact]
    public async Task RemovesLegacyPinWithoutRewritingOtherLegacyProperties()
    {
        var path = Path.Combine(_directory, "global.json");
        await File.WriteAllTextAsync(path, """
        {
          // Older Sherpa shape.
          "sdk": {
            "version": "10.0.302"
          },
          "workloadSet": {
            "version": "10.0.300.1",
            "custom": true,
          }
        }
        """);
        var editor = new GlobalJsonWorkloadPinEditor();

        await editor.ApplyAsync(editor.Preview(_directory, null));
        var updated = await File.ReadAllTextAsync(path);

        updated.Should().Contain("// Older Sherpa shape.");
        updated.Should().NotContain("\"version\": \"10.0.300.1\"");
        updated.Should().Contain("\"custom\": true");
        new GlobalJsonService().ParseGlobalJson(path)!.WorkloadSetVersion.Should().BeNull();
    }

    [Fact]
    public async Task AddsPinAfterPropertyWithTrailingLineComment()
    {
        var path = Path.Combine(_directory, "global.json");
        await File.WriteAllTextAsync(path, """
        {
          "sdk": {
            "version": "10.0.302" // Keep the SDK explanation.
          }
        }
        """);
        var editor = new GlobalJsonWorkloadPinEditor();

        await editor.ApplyAsync(editor.Preview(_directory, "10.0.300.1"));
        var updated = await File.ReadAllTextAsync(path);

        updated.Should().Contain("\"version\": \"10.0.302\", // Keep the SDK explanation.");
        updated.Should().Contain("\"workloadVersion\": \"10.0.300.1\"");
        ParseJsonc(updated).RootElement.GetProperty("sdk").GetProperty("workloadVersion")
            .GetString().Should().Be("10.0.300.1");
    }

    [Fact]
    public async Task RemovesLastPinWithoutRemovingPreviousPropertyComment()
    {
        var path = Path.Combine(_directory, "global.json");
        await File.WriteAllTextAsync(path, """
        {
          "sdk": {
            "version": "10.0.302", // Keep the SDK explanation.
            "workloadVersion": "10.0.300.1"
          }
        }
        """);
        var editor = new GlobalJsonWorkloadPinEditor();

        await editor.ApplyAsync(editor.Preview(_directory, null));
        var updated = await File.ReadAllTextAsync(path);

        updated.Should().Contain("// Keep the SDK explanation.");
        updated.Should().NotContain("workloadVersion");
        ParseJsonc(updated).RootElement.GetProperty("sdk").GetProperty("version")
            .GetString().Should().Be("10.0.302");
    }

    [Fact]
    public async Task RemovesPinWithBlockCommentBeforeFollowingComma()
    {
        var path = Path.Combine(_directory, "global.json");
        await File.WriteAllTextAsync(path, """
        {
          "sdk": {
            "version": "10.0.302",
            "workloadVersion": "10.0.300.1" /* old pin */,
            "rollForward": "latestPatch"
          }
        }
        """);
        var editor = new GlobalJsonWorkloadPinEditor();

        await editor.ApplyAsync(editor.Preview(_directory, null));
        var updated = await File.ReadAllTextAsync(path);

        updated.Should().NotContain("workloadVersion");
        ParseJsonc(updated).RootElement.GetProperty("sdk").GetProperty("rollForward")
            .GetString().Should().Be("latestPatch");
    }

    [Fact]
    public async Task IgnoresNestedPropertiesAfterSdkObject()
    {
        var path = Path.Combine(_directory, "global.json");
        await File.WriteAllTextAsync(path, """
        {
          "sdk": {
            "version": "10.0.302"
          },
          "msbuild-sdks": {
            "Custom.Sdk": {
              "workloadVersion": "leave-me-alone"
            }
          }
        }
        """);
        var editor = new GlobalJsonWorkloadPinEditor();

        await editor.ApplyAsync(editor.Preview(_directory, "10.0.300.1"));
        var updated = await File.ReadAllTextAsync(path);

        ParseJsonc(updated).RootElement
            .GetProperty("sdk")
            .GetProperty("workloadVersion")
            .GetString()
            .Should().Be("10.0.300.1");
        updated.Should().Contain("\"workloadVersion\": \"leave-me-alone\"");
    }

    [Fact]
    public async Task RejectsApplyWhenGlobalJsonChangedAfterPreview()
    {
        var path = Path.Combine(_directory, "global.json");
        await File.WriteAllTextAsync(path, """{"sdk":{"version":"10.0.302"}}""");
        var editor = new GlobalJsonWorkloadPinEditor();
        var preview = editor.Preview(_directory, "10.0.300.1");
        await File.WriteAllTextAsync(path, """{"sdk":{"version":"10.0.303"}}""");

        var action = () => editor.ApplyAsync(preview);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*changed after the workload pin preview*");
        (await File.ReadAllTextAsync(path)).Should().Contain("10.0.303");
    }

    private static JsonDocument ParseJsonc(string content) =>
        JsonDocument.Parse(content, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}

using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public class CopilotModelSelectorTests
{
    [Fact]
    public void SelectPreferred_PicksSolBeforeTerraForSameVersion()
    {
        CopilotModelOption[] models =
        [
            new("auto", "Auto"),
            new("gpt-5.6-terra", "GPT-5.6 Terra"),
            new("gpt-5.6-sol", "GPT-5.6 Sol"),
            new("gpt-5.5", "GPT-5.5")
        ];

        CopilotModelSelector.SelectPreferred(models).Should().Be("gpt-5.6-sol");
    }

    [Fact]
    public void SelectPreferred_PicksNewerGptVersionBeforePreferredVariant()
    {
        CopilotModelOption[] models =
        [
            new("gpt-5.6-sol", "GPT-5.6 Sol"),
            new("gpt-5.7-luna", "GPT-5.7 Luna")
        ];

        CopilotModelSelector.SelectPreferred(models).Should().Be("gpt-5.7-luna");
    }

    [Fact]
    public void SelectPreferred_FallsBackToAutoWithoutGptModels()
    {
        CopilotModelOption[] models =
        [
            new("auto", "Auto"),
            new("claude-sonnet-5", "Claude Sonnet 5")
        ];

        CopilotModelSelector.SelectPreferred(models).Should().Be("auto");
    }
}

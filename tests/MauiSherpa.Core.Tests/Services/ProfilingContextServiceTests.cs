using FluentAssertions;
using MauiSherpa.Core.Models.DevFlow;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public class ProfilingContextServiceTests
{
    [Fact]
    public void BuildNetworkSummary_UsesMostRecentRequestsAndCalculatesMetrics()
    {
        var now = DateTimeOffset.UtcNow;
        var requests = new[]
        {
            new DevFlowNetworkRequest
            {
                Timestamp = now.AddMinutes(-10),
                Method = "GET",
                Url = "https://example.com/old",
                DurationMs = 15,
                StatusCode = 200,
                RequestSize = 1,
                ResponseSize = 10
            },
            new DevFlowNetworkRequest
            {
                Timestamp = now.AddMinutes(-1),
                Method = "GET",
                Url = "https://example.com/fast",
                DurationMs = 120,
                StatusCode = 200,
                RequestSize = 20,
                ResponseSize = 100
            },
            new DevFlowNetworkRequest
            {
                Timestamp = now.AddMinutes(-2),
                Method = "POST",
                Url = "https://example.com/slow",
                DurationMs = 450,
                StatusCode = 500,
                RequestSize = 30,
                ResponseSize = 0
            },
            new DevFlowNetworkRequest
            {
                Timestamp = now.AddMinutes(-3),
                Method = "GET",
                Url = "https://example.com/error",
                DurationMs = 240,
                Error = "timeout",
                RequestSize = 40,
                ResponseSize = 5
            }
        };

        var summary = ProfilingContextService.BuildNetworkSummary(requests, 3);

        summary.SampleSize.Should().Be(3);
        summary.SuccessCount.Should().Be(1);
        summary.FailureCount.Should().Be(2);
        summary.AverageDurationMs.Should().BeApproximately(270, 0.001);
        summary.P95DurationMs.Should().BeGreaterThan(400);
        summary.MaxDurationMs.Should().Be(450);
        summary.TotalRequestBytes.Should().Be(90);
        summary.TotalResponseBytes.Should().Be(105);
        summary.SlowestRequests.Select(r => r.Url).Should().ContainInOrder(
            "https://example.com/slow",
            "https://example.com/error",
            "https://example.com/fast");
    }

    [Fact]
    public void BuildVisualTreeSummary_FlattensTreeAndCalculatesCounts()
    {
        var roots = new[]
        {
            new DevFlowElementInfo
            {
                Id = "root",
                Type = "Grid",
                IsVisible = true,
                Children = new List<DevFlowElementInfo>
                {
                    new()
                    {
                        Id = "button",
                        Type = "Button",
                        IsVisible = true,
                        Gestures = new List<string> { "Tap" }
                    },
                    new()
                    {
                        Id = "stack",
                        Type = "VerticalStackLayout",
                        IsVisible = true,
                        Children = new List<DevFlowElementInfo>
                        {
                            new()
                            {
                                Id = "entry",
                                Type = "Entry",
                                IsVisible = false,
                                IsFocused = true,
                                Gestures = new List<string> { "Focus" }
                            }
                        }
                    }
                }
            }
        };

        var summary = ProfilingContextService.BuildVisualTreeSummary(roots);

        summary.RootCount.Should().Be(1);
        summary.TotalElementCount.Should().Be(4);
        summary.VisibleElementCount.Should().Be(3);
        summary.FocusedElementCount.Should().Be(1);
        summary.InteractiveElementCount.Should().Be(2);
        summary.MaxDepth.Should().Be(3);
        summary.TopElementTypes.Should().ContainEquivalentOf(new { Type = "Grid", Count = 1 });
        summary.TopElementTypes.Should().ContainEquivalentOf(new { Type = "Button", Count = 1 });
        summary.TopElementTypes.Should().ContainEquivalentOf(new { Type = "Entry", Count = 1 });
    }
}

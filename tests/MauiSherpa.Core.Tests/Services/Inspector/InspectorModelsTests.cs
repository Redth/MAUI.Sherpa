using System.Text.Json;
using FluentAssertions;
using MauiSherpa.Core.Models.Inspector;

namespace MauiSherpa.Core.Tests.Services.Inspector;

/// <summary>
/// Validates that v1 JSON responses deserialize into the common inspector models.
/// </summary>
public class InspectorModelsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void Capabilities_HasCapability_MatchesRegisteredNamespaces()
    {
        var caps = new InspectorCapabilities
        {
            Capabilities = new Dictionary<string, CapabilityDetail>
            {
                ["ui.tree"] = new() { Version = 1, Features = ["find", "query"] },
                ["ui.actions"] = new() { Version = 1, Features = ["tap", "fill"] },
            }
        };

        caps.HasCapability("ui.tree").Should().BeTrue();
        caps.HasCapability("ui.actions").Should().BeTrue();
        caps.HasCapability("profiler").Should().BeFalse();
    }

    [Fact]
    public void Capabilities_HasFeature_MatchesFeatureList()
    {
        var caps = new InspectorCapabilities
        {
            Capabilities = new Dictionary<string, CapabilityDetail>
            {
                ["ui.actions"] = new() { Version = 1, Features = ["tap", "fill", "scroll"] },
            }
        };

        caps.HasFeature("ui.actions", "tap").Should().BeTrue();
        caps.HasFeature("ui.actions", "gesture").Should().BeFalse();
        caps.HasFeature("unknown", "tap").Should().BeFalse();
    }

    [Fact]
    public void ElementState_DeserializesFromV1Json()
    {
        const string json = """
        {
            "displayed": true,
            "enabled": false,
            "selected": false,
            "focused": true,
            "opacity": 0.5
        }
        """;

        var state = JsonSerializer.Deserialize<ElementState>(json, JsonOptions);

        state.Should().NotBeNull();
        state!.Displayed.Should().BeTrue();
        state.Enabled.Should().BeFalse();
        state.Focused.Should().BeTrue();
        state.Opacity.Should().Be(0.5);
    }

    [Fact]
    public void InspectorElement_DeserializesFromV1Json()
    {
        const string json = """
        {
            "id": "btn-1",
            "parentId": "page-1",
            "type": "Button",
            "fullType": "Microsoft.Maui.Controls.Button",
            "framework": "maui",
            "automationId": "submit",
            "text": "Submit",
            "value": null,
            "role": "button",
            "traits": ["interactive", "focusable"],
            "state": {
                "displayed": true,
                "enabled": true,
                "focused": false,
                "opacity": 1.0
            },
            "bounds": {
                "x": 10,
                "y": 20,
                "width": 100,
                "height": 40,
                "coordinateSystem": "window"
            }
        }
        """;

        var element = JsonSerializer.Deserialize<InspectorElement>(json, JsonOptions);

        element.Should().NotBeNull();
        element!.Id.Should().Be("btn-1");
        element.ParentId.Should().Be("page-1");
        element.Framework.Should().Be("maui");
        element.Role.Should().Be("button");
        element.Traits.Should().BeEquivalentTo(["interactive", "focusable"]);
        element.State.Displayed.Should().BeTrue();
        element.State.Opacity.Should().Be(1.0);
        element.Bounds.Should().NotBeNull();
        element.Bounds!.X.Should().Be(10);
        element.Bounds.CoordinateSystem.Should().Be("window");
    }

    [Fact]
    public void InspectorError_DeserializesRfc7807ProblemDetails()
    {
        const string json = """
        {
            "type": "about:blank",
            "title": "Element not found",
            "status": 404,
            "detail": "No element with id 'xyz'",
            "errorCode": "element-not-found"
        }
        """;

        var error = JsonSerializer.Deserialize<InspectorError>(json, JsonOptions);

        error.Should().NotBeNull();
        error!.Title.Should().Be("Element not found");
        error.Status.Should().Be(404);
        error.ErrorCode.Should().Be("element-not-found");
    }

    [Fact]
    public void AgentStatus_DeserializesFromV1Json()
    {
        const string json = """
        {
            "agent": {
                "name": "devflow-maui",
                "version": "1.0.0",
                "framework": "maui",
                "frameworkVersion": "10.0"
            },
            "platform": "MacCatalyst",
            "device": {
                "model": "Mac",
                "manufacturer": "Apple",
                "osVersion": "15.0",
                "idiom": "desktop"
            },
            "app": {
                "name": "MyApp",
                "version": "1.0",
                "packageId": "com.example.myapp"
            },
            "running": true
        }
        """;

        var status = JsonSerializer.Deserialize<InspectorAgentStatus>(json, JsonOptions);

        status.Should().NotBeNull();
        status!.Agent.Name.Should().Be("devflow-maui");
        status.Agent.Framework.Should().Be("maui");
        status.Platform.Should().Be("MacCatalyst");
        status.Device.Model.Should().Be("Mac");
        status.App.PackageId.Should().Be("com.example.myapp");
        status.Running.Should().BeTrue();
    }

    [Fact]
    public void ProtocolVersion_EnumValues()
    {
        Enum.GetValues<InspectorProtocolVersion>().Should().Contain([
            InspectorProtocolVersion.Legacy,
            InspectorProtocolVersion.V1,
        ]);
    }
}

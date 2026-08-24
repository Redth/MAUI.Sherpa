using FluentAssertions;

namespace MauiSherpa.Bundles.Tests.Build;

public class BundleBuildServiceEnvironmentTests
{
    private static readonly IReadOnlyDictionary<string, string> Empty =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void BuildProcessEnvironment_MergesPreparationVariablesAndProperties_WithPropertiesWinning()
    {
        var preparation = new Dictionary<string, string> { ["DEVELOPER_DIR"] = "/Applications/Xcode.app" };
        var variables = new Dictionary<string, string>
        {
            ["ApplicationId"] = "com.contoso.mobile",
            ["BuildNumber"] = "42"
        };
        var properties = new Dictionary<string, string>
        {
            // Properties expand against `variables` and override same-named entries.
            ["ApplicationVersion"] = "1.4.${BuildNumber}"
        };

        var environment = BundleBuildService.BuildProcessEnvironment(preparation, variables, properties);

        environment["DEVELOPER_DIR"].Should().Be("/Applications/Xcode.app");
        environment["ApplicationId"].Should().Be("com.contoso.mobile");
        environment["ApplicationVersion"].Should().Be("1.4.42");
    }

    [Fact]
    public void BuildProcessEnvironment_PropertyOverridesVariableOfSameName()
    {
        var variables = new Dictionary<string, string> { ["ApplicationId"] = "com.contoso.mobile" };
        var properties = new Dictionary<string, string> { ["ApplicationId"] = "com.contoso.mobile.beta" };

        var environment = BundleBuildService.BuildProcessEnvironment(Empty, variables, properties);

        environment["ApplicationId"].Should().Be("com.contoso.mobile.beta");
    }

    [Fact]
    public void BuildProcessEnvironment_WithUnresolvedPropertyReference_ThrowsBundleValidationException()
    {
        var properties = new Dictionary<string, string> { ["ApplicationVersion"] = "1.0.${MissingBuildNumber}" };

        var act = () => BundleBuildService.BuildProcessEnvironment(Empty, Empty, properties);

        act.Should().Throw<BundleValidationException>();
    }

    [Theory]
    [InlineData("Api-Base-Url")]
    [InlineData("My.Var")]
    [InlineData("1StartsWithDigit")]
    [InlineData("")]
    public void BuildProcessEnvironment_WithPropertyNameThatIsNotAValidMSBuildIdentifier_Throws(string propertyName)
    {
        var properties = new Dictionary<string, string> { [propertyName] = "value" };

        var act = () => BundleBuildService.BuildProcessEnvironment(Empty, Empty, properties);

        act.Should().Throw<BundleValidationException>();
    }

    [Theory]
    [InlineData("ApplicationId")]
    [InlineData("_Underscore")]
    [InlineData("Value1")]
    public void BuildProcessEnvironment_WithValidMSBuildPropertyName_Succeeds(string propertyName)
    {
        var properties = new Dictionary<string, string> { [propertyName] = "value" };

        var environment = BundleBuildService.BuildProcessEnvironment(Empty, Empty, properties);

        environment[propertyName].Should().Be("value");
    }

    [Fact]
    public void BuildProcessEnvironment_WithVariableNameContainingEquals_Throws()
    {
        // A variable name containing '=' cannot be represented as a single environment variable
        // assignment; this must fail fast with a clear validation error instead of a raw
        // Process.Start/ArgumentException deep inside the process runner.
        var variables = new Dictionary<string, string> { ["Bad=Name"] = "value" };

        var act = () => BundleBuildService.BuildProcessEnvironment(Empty, variables, Empty);

        act.Should().Throw<BundleValidationException>();
    }

    [Fact]
    public void BuildProcessEnvironment_AllowsNonMSBuildIdentifierVariableNames()
    {
        // General bundle variables are not required to be valid MSBuild property identifiers -
        // only entries under configuration.Properties are, since those are explicitly meant to
        // become MSBuild properties.
        var variables = new Dictionary<string, string> { ["Api-Base-Url"] = "https://api.example.com" };

        var environment = BundleBuildService.BuildProcessEnvironment(Empty, variables, Empty);

        environment["Api-Base-Url"].Should().Be("https://api.example.com");
    }
}

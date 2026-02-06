using Shiny.Mediator;

namespace MauiSherpa.Core.Requests.Android;

/// <summary>
/// Request to detect/get the Android SDK path.
/// Result is the SDK path if found, null otherwise.
/// </summary>
public record GetSdkPathRequest : IRequest<string?>, IContractKey
{
    public string GetKey() => "android:sdkpath";
}

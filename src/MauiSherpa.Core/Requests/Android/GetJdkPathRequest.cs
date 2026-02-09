using Shiny.Mediator;

namespace MauiSherpa.Core.Requests.Android;

/// <summary>
/// Request to detect/get the OpenJDK path.
/// Result is the JDK path if found, null otherwise.
/// </summary>
public record GetJdkPathRequest : IRequest<string?>, IContractKey
{
    public string GetKey() => "android:jdkpath";
}

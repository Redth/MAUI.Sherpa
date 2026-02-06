using Shiny.Mediator;

namespace MauiSherpa.Core.Requests.Android;

/// <summary>
/// Request to get available system images for creating emulators
/// </summary>
public record GetSystemImagesRequest : IRequest<IReadOnlyList<string>>, IContractKey
{
    public string GetKey() => "android:systemimages";
}

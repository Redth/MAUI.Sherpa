using Shiny.Mediator;

namespace MauiSherpa.Core.Requests.Apple;

/// <summary>
/// Command to track when a bundle ID is used (e.g., in CI Secrets wizard) for a specific Apple identity
/// </summary>
public record TrackBundleIdUsageRequest(string IdentityId, string BundleIdIdentifier) : ICommand;

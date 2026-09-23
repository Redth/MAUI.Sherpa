using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Sherpa's identity for the DevFlow agent's mutation lease. Agents enforce the lease by default
/// (<c>AgentOptions.RequireMutationLease</c>): every v1 PUT/POST/DELETE is rejected with
/// 409 Conflict (reason <c>lease</c>) unless the caller has claimed it.
/// </summary>
public sealed class DevFlowMutationLease
{
    public const string LeasePath = "/api/v1/agent/lease";
    private const string HolderKind = "inspector";
    private const string Label = "MAUI Sherpa";

    private volatile bool _unsupported;

    public string LeaseId { get; } = Guid.NewGuid().ToString("N");

    public void ApplyHeaders(HttpRequestHeaders headers)
    {
        headers.Remove("X-DevFlow-Lease");
        headers.TryAddWithoutValidation("X-DevFlow-Lease", LeaseId);
        headers.Remove("X-DevFlow-Holder");
        headers.TryAddWithoutValidation("X-DevFlow-Holder", HolderKind);
        headers.Remove("X-DevFlow-Label");
        headers.TryAddWithoutValidation("X-DevFlow-Label", Label);
    }

    public static bool RequiresLease(HttpMethod method, Uri uri) =>
        method != HttpMethod.Get
        && method != HttpMethod.Head
        && uri.AbsolutePath.StartsWith("/api/v1/", StringComparison.Ordinal)
        && !uri.AbsolutePath.Equals(LeasePath, StringComparison.Ordinal);

    /// <summary>
    /// Claims the lease, or renews it when Sherpa already holds it (the claim doubles as the
    /// heartbeat that keeps it from expiring). Claims never take the lease from another session;
    /// in that case the mutation itself fails with the agent's descriptive 409.
    /// </summary>
    public async Task ClaimAsync(
        Uri agentUri,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        CancellationToken ct)
    {
        if (_unsupported)
            return;

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(agentUri, LeasePath))
        {
            Content = JsonContent.Create(new { action = "claim", leaseId = LeaseId, holderKind = HolderKind, label = Label })
        };
        ApplyHeaders(request.Headers);

        using var response = await send(request, ct);
        // Agents that predate mutation leases have no lease endpoint and accept mutations as-is.
        if (response.StatusCode == HttpStatusCode.NotFound)
            _unsupported = true;
    }
}

/// <summary>
/// Claims the <see cref="DevFlowMutationLease"/> before each v1 mutating request.
/// </summary>
public sealed class DevFlowMutationLeaseHandler : DelegatingHandler
{
    private readonly DevFlowMutationLease _lease;

    public DevFlowMutationLeaseHandler(DevFlowMutationLease lease, HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
        _lease = lease;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _lease.ApplyHeaders(request.Headers);
        if (request.RequestUri is { } uri && DevFlowMutationLease.RequiresLease(request.Method, uri))
            await _lease.ClaimAsync(uri, (message, ct) => base.SendAsync(message, ct), cancellationToken);

        return await base.SendAsync(request, cancellationToken);
    }
}

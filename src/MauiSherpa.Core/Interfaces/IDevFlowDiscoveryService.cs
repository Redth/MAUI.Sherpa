using MauiSherpa.Core.Models.DevFlow;

namespace MauiSherpa.Core.Interfaces;

public record DevFlowDiscoveryResult(bool BrokerConnected, IReadOnlyList<BrokerAgent> Agents);

public interface IDevFlowDiscoveryService
{
    Task<DevFlowDiscoveryResult> DiscoverAsync(string host, int brokerPort);
    Task<DevFlowAgentStatus?> GetStatusAsync(string host, int agentPort);
}

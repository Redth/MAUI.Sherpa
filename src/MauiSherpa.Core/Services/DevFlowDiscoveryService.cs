using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.DevFlow;

namespace MauiSherpa.Core.Services;

public sealed class DevFlowDiscoveryService : IDevFlowDiscoveryService
{
    public async Task<DevFlowDiscoveryResult> DiscoverAsync(string host, int brokerPort)
    {
        var ports = DevFlowAgentClient.DefaultBrokerPorts.Prepend(brokerPort).Distinct();
        var probes = await Task.WhenAll(ports.Select(async port =>
            (Port: port, Healthy: await DevFlowAgentClient.IsBrokerHealthyAsync(host, port))));
        var healthyPorts = probes.Where(p => p.Healthy).Select(p => p.Port).ToArray();
        var lists = await Task.WhenAll(healthyPorts.Select(port => DevFlowAgentClient.GetBrokerAgentsAsync(host, port)));
        var agents = lists.SelectMany(list => list).ToArray();
        await Task.WhenAll(agents.Select(async agent =>
        {
            try
            {
                var status = await GetStatusAsync(host, agent.Port);
                if (status?.Version is { } version)
                    agent.Version = version;
            }
            catch (HttpRequestException)
            {
                // Broker entries can outlive an agent.
            }
            catch (TaskCanceledException)
            {
                // An unresponsive agent should not hide the rest of the inventory.
            }
        }));
        return new(healthyPorts.Length > 0, agents);
    }

    public async Task<DevFlowAgentStatus?> GetStatusAsync(string host, int agentPort)
    {
        using var client = new DevFlowAgentClient(host, agentPort);
        return await client.GetStatusAsync();
    }
}

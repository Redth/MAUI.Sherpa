using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public static class SimulatorDeletion
{
    public static async Task<bool> RunAsync(ISimulatorService service, SimulatorDevice simulator, IOperationContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        context.LogInfo($"Simulator: {simulator.Name}");
        context.LogInfo($"UDID: {simulator.Udid}");

        var current = (await service.GetSimulatorsAsync()).FirstOrDefault(s => s.Udid == simulator.Udid);
        if (current == null)
        {
            context.LogError("The simulator could not be found. Refresh simulators before trying again.");
            return false;
        }

        var progress = new Progress<string>(message =>
        {
            context.LogInfo(message);
            context.SetStatus(message);
        });

        context.CancellationToken.ThrowIfCancellationRequested();
        if (current.State == "Booted")
        {
            context.SetStatus("Shutting down simulator...");
            if (!await service.ShutdownSimulatorAsync(simulator.Udid, progress))
            {
                context.LogError("Could not shut down the simulator. It was not deleted.");
                return false;
            }
        }
        else if (current.State != "Shutdown")
        {
            context.LogError($"The simulator is currently '{current.State}'. Wait for it to finish changing state, then try again.");
            return false;
        }

        context.CancellationToken.ThrowIfCancellationRequested();
        context.SetStatus("Deleting simulator...");
        var success = await service.DeleteSimulatorAsync(simulator.Udid, progress);
        if (success)
            context.LogSuccess($"Simulator '{simulator.Name}' deleted");
        else
            context.LogError($"Could not delete '{simulator.Name}'. Refresh simulators before trying again.");
        return success;
    }
}

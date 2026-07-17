using FluentAssertions;
using MauiSherpa.Workloads.Services;

namespace MauiSherpa.Workloads.Tests.Services;

public class GlobalJsonServiceTests
{
    [Fact]
    public void ParseGlobalJson_ReadsWorkloadsUpdateMode()
    {
        var path = Path.Combine(Path.GetTempPath(), $"global-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """
            {
              "sdk": {
                "version": "10.0.302",
                "workloadVersion": "10.0.300.1",
                "workloads-update-mode": "workload-set"
              }
            }
            """);

            var result = new GlobalJsonService().ParseGlobalJson(path);

            result.Should().NotBeNull();
            result!.WorkloadSetVersion.Should().Be("10.0.300.1");
            result.WorkloadsUpdateMode.Should().Be("workload-set");
        }
        finally
        {
            File.Delete(path);
        }
    }
}

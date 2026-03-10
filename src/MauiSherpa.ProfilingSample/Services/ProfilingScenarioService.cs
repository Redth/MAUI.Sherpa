using System.Diagnostics;
using MauiSherpa.ProfilingSample.Models;

namespace MauiSherpa.ProfilingSample.Services;

public sealed class ProfilingScenarioService : IDisposable
{
    private readonly object _memoryGate = new();
    private readonly object _renderGate = new();
    private readonly List<MemoryChunk> _retainedMemory = [];
    private readonly Random _random = new(4172);

    private CancellationTokenSource? _cpuCts;
    private CancellationTokenSource? _memoryChurnCts;
    private Timer? _animationTimer;
    private long _cpuIterations;
    private int _cpuRunId;
    private int _nextFeedId = 1;
    private int _nextChunkId = 1;

    public event Action? Changed;

    public CpuScenarioState Cpu { get; } = new();

    public MemoryScenarioState Memory { get; } = new();

    public RenderingScenarioState Rendering { get; } = new();

    public NetworkScenarioState Network { get; } = new();

    public ProfilingScenarioService()
    {
        ResetRenderingScene();
        RefreshMemoryState("Ready to retain or churn allocations.");
        Network.Status = "Run a local or remote burst to generate visible request traffic.";
    }

    public Task StartCpuStressAsync(int workerCount, TimeSpan duration)
    {
        StopCpuStress();

        var runId = Interlocked.Increment(ref _cpuRunId);
        var tokenSource = new CancellationTokenSource();
        _cpuCts = tokenSource;
        Interlocked.Exchange(ref _cpuIterations, 0);

        Cpu.IsRunning = true;
        Cpu.WorkerCount = Math.Clamp(workerCount, 1, Math.Max(1, Environment.ProcessorCount));
        Cpu.DurationSeconds = Math.Clamp((int)Math.Ceiling(duration.TotalSeconds), 5, 180);
        Cpu.StartedAt = DateTimeOffset.UtcNow;
        Cpu.Status = $"Crunching prime checks with {Cpu.WorkerCount} worker(s).";
        Cpu.LastBatchMilliseconds = 0;
        Cpu.IterationsCompleted = 0;
        NotifyChanged();

        var tasks = Enumerable.Range(0, Cpu.WorkerCount)
            .Select(worker => Task.Run(() => RunCpuWorker(worker, tokenSource.Token), tokenSource.Token))
            .ToArray();

        _ = Task.Run(async () =>
        {
            var completedNaturally = false;

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Cpu.DurationSeconds), tokenSource.Token);
                completedNaturally = true;
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                tokenSource.Cancel();
            }

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
            }

            if (runId != Volatile.Read(ref _cpuRunId))
            {
                return;
            }

            Cpu.IsRunning = false;
            Cpu.IterationsCompleted = Interlocked.Read(ref _cpuIterations);
            Cpu.Status = completedNaturally
                ? "Scheduled CPU run completed."
                : "CPU run stopped.";
            NotifyChanged();
            tokenSource.Dispose();
        });

        return Task.CompletedTask;
    }

    public void StopCpuStress()
    {
        _cpuCts?.Cancel();
        _cpuCts = null;

        if (Cpu.IsRunning)
        {
            Cpu.IsRunning = false;
            Cpu.Status = "CPU run stopped.";
            NotifyChanged();
        }
    }

    public void AllocateRetainedMemory(int megabytes)
    {
        var clampedMegabytes = Math.Clamp(megabytes, 8, 512);

        lock (_memoryGate)
        {
            _retainedMemory.Add(CreateChunk(clampedMegabytes));
            RefreshMemoryState($"Retained another {clampedMegabytes} MB across object graphs and byte buffers.");
        }

        NotifyChanged();
    }

    public void ReleaseHalfMemory()
    {
        lock (_memoryGate)
        {
            var removeCount = _retainedMemory.Count / 2;
            if (removeCount > 0)
            {
                _retainedMemory.RemoveRange(0, removeCount);
            }

            RefreshMemoryState(removeCount == 0
                ? "Nothing to release yet."
                : $"Released {removeCount} retained chunk(s).");
        }

        NotifyChanged();
    }

    public void ClearRetainedMemory()
    {
        lock (_memoryGate)
        {
            _retainedMemory.Clear();
            RefreshMemoryState("Cleared retained allocations.");
        }

        NotifyChanged();
    }

    public void CaptureMemorySnapshot()
    {
        lock (_memoryGate)
        {
            var snapshot = new MemorySnapshot(
                $"snap-{DateTimeOffset.UtcNow:HHmmssfff}",
                DateTimeOffset.Now,
                Memory.RetainedBytes,
                GC.GetTotalMemory(false),
                Memory.ChunkCount);

            Memory.Snapshots = Memory.Snapshots
                .Prepend(snapshot)
                .Take(6)
                .ToArray();

            Memory.Status = "Captured a point-in-time heap snapshot marker.";
        }

        NotifyChanged();
    }

    public void ToggleMemoryChurn()
    {
        if (Memory.IsChurning)
        {
            StopMemoryChurn();
            return;
        }

        _memoryChurnCts?.Cancel();
        var churnCts = new CancellationTokenSource();
        _memoryChurnCts = churnCts;
        Memory.IsChurning = true;
        Memory.Status = "Background churn is allocating and trimming every 350 ms.";
        NotifyChanged();

        _ = Task.Run(async () =>
        {
            while (!churnCts.IsCancellationRequested)
            {
                lock (_memoryGate)
                {
                    _retainedMemory.Add(CreateChunk(8));
                    if (_retainedMemory.Count > 18)
                    {
                        _retainedMemory.RemoveAt(0);
                    }

                    RefreshMemoryState("Background churn is allocating and trimming every 350 ms.");
                }

                NotifyChanged();

                try
                {
                    await Task.Delay(350, churnCts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            churnCts.Dispose();
        });
    }

    public void StopMemoryChurn()
    {
        _memoryChurnCts?.Cancel();
        _memoryChurnCts = null;
        Memory.IsChurning = false;
        RefreshMemoryState("Memory churn stopped.");
        NotifyChanged();
    }

    public void AddTiles(int count)
    {
        lock (_renderGate)
        {
            var targetCount = Math.Clamp(Rendering.Tiles.Count + count, 0, 480);
            Rendering.Tiles = BuildTiles(targetCount, Rendering.Frame);
            Rendering.Status = $"Visual tile wall now contains {targetCount} animated nodes.";
        }

        NotifyChanged();
    }

    public void AddFeedItems(int count)
    {
        lock (_renderGate)
        {
            var updated = Rendering.FeedItems.ToList();
            updated.AddRange(BuildFeedItems(count));
            Rendering.FeedItems = updated;
            Rendering.Status = $"Scrollable feed expanded to {updated.Count} rows.";
        }

        NotifyChanged();
    }

    public void ResetRenderingScene()
    {
        lock (_renderGate)
        {
            Rendering.Frame = 0;
            Rendering.Tiles = BuildTiles(12, 0);
            Rendering.FeedItems = BuildFeedItems(6);
            Rendering.Status = "Scene ready — use the buttons to add tiles and feed items.";
        }

        NotifyChanged();
    }

    public void ToggleRenderingAnimation()
    {
        if (Rendering.IsAnimating)
        {
            _animationTimer?.Dispose();
            _animationTimer = null;
            Rendering.IsAnimating = false;
            Rendering.Status = "Animation paused. Scroll the feed to inspect the dense visual tree.";
            NotifyChanged();
            return;
        }

        Rendering.IsAnimating = true;
        Rendering.Status = "Animating the tile wall at ~14 FPS for render churn.";
        _animationTimer?.Dispose();
        _animationTimer = new Timer(_ => AdvanceAnimationFrame(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(70));
        NotifyChanged();
    }

    public void BeginNetworkRun(string mode, int requestCount)
    {
        Network.IsRunning = true;
        Network.LastMode = mode;
        Network.Status = $"Running {requestCount} {mode} request(s) from the Blazor WebView...";
        NotifyChanged();
    }

    public void CompleteNetworkRun(string mode, IReadOnlyList<NetworkRunResult> results)
    {
        var recent = results
            .OrderByDescending(r => r.DurationMs)
            .Take(12)
            .ToArray();

        Network.IsRunning = false;
        Network.LastMode = mode;
        Network.SuccessCount = results.Count(r => r.Success);
        Network.FailureCount = results.Count - Network.SuccessCount;
        Network.AverageDurationMs = results.Count == 0 ? 0 : results.Average(r => r.DurationMs);
        Network.TotalBytes = results.Sum(r => r.Bytes);
        Network.RecentRequests = recent;
        Network.Status = results.Count == 0
            ? "No requests were recorded."
            : $"Captured {results.Count} request(s) with {Network.FailureCount} failure(s).";
        NotifyChanged();
    }

    public void FailNetworkRun(string mode, string error)
    {
        Network.IsRunning = false;
        Network.LastMode = mode;
        Network.Status = $"Network run failed before completion: {error}";
        NotifyChanged();
    }

    /// <summary>
    /// Runs a network burst using HttpClient (for native MAUI UI, no JS/WebView needed).
    /// </summary>
    public async Task RunNativeNetworkBurstAsync(string mode, int requestCount)
    {
        requestCount = Math.Clamp(requestCount, 1, 40);
        BeginNetworkRun(mode, requestCount);

        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var tasks = Enumerable.Range(0, requestCount)
                .Select(i => ExecuteNativeRequest(httpClient, mode, i))
                .ToArray();

            var results = await Task.WhenAll(tasks);
            CompleteNetworkRun(mode, results);
        }
        catch (Exception ex)
        {
            FailNetworkRun(mode, ex.Message);
        }
    }

    private static async Task<NetworkRunResult> ExecuteNativeRequest(HttpClient client, string mode, int index)
    {
        var url = mode == "remote-mixed"
            ? (index % 4) switch
            {
                0 => $"https://jsonplaceholder.typicode.com/todos/{(index % 10) + 1}",
                1 => $"https://httpbin.org/delay/1",
                2 => $"https://jsonplaceholder.typicode.com/invalid-route-{index}",
                _ => $"https://httpbin.org/status/503"
            }
            : $"https://jsonplaceholder.typicode.com/posts/{(index % 50) + 1}";

        var sw = Stopwatch.StartNew();
        try
        {
            using var response = await client.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();
            sw.Stop();
            return new NetworkRunResult
            {
                Url = url,
                StatusCode = (int)response.StatusCode,
                Success = response.IsSuccessStatusCode,
                DurationMs = (int)sw.ElapsedMilliseconds,
                Error = response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}",
                Bytes = body.Length
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new NetworkRunResult
            {
                Url = url,
                StatusCode = 0,
                Success = false,
                DurationMs = (int)sw.ElapsedMilliseconds,
                Error = ex.Message,
                Bytes = 0
            };
        }
    }

    private void RunCpuWorker(int worker, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        while (!cancellationToken.IsCancellationRequested)
        {
            var score = 0d;
            var primeHits = 0;

            for (var candidate = 10_000 + (worker * 137); candidate < 14_500 + (worker * 137); candidate++)
            {
                if (!IsPrime(candidate))
                {
                    continue;
                }

                primeHits++;
                score += Math.Sqrt(candidate) * Math.Sin(candidate / 13d);
            }

            var accumulator = new double[128];
            for (var index = 0; index < accumulator.Length; index++)
            {
                accumulator[index] = Math.Log10(index + 2 + score) * Math.Cos(index + worker);
            }

            accumulator.Sort();

            Interlocked.Add(ref _cpuIterations, primeHits + accumulator.Length);
            Cpu.IterationsCompleted = Interlocked.Read(ref _cpuIterations);
            Cpu.LastBatchMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            Cpu.Status = $"Latest worker batch finished in {Cpu.LastBatchMilliseconds:F1} ms.";
            stopwatch.Restart();

            if (worker == 0)
            {
                NotifyChanged();
            }
        }
    }

    private bool IsPrime(int value)
    {
        if (value < 2)
        {
            return false;
        }

        if (value % 2 == 0)
        {
            return value == 2;
        }

        var limit = (int)Math.Sqrt(value);
        for (var divisor = 3; divisor <= limit; divisor += 2)
        {
            if (value % divisor == 0)
            {
                return false;
            }
        }

        return true;
    }

    private void AdvanceAnimationFrame()
    {
        lock (_renderGate)
        {
            var nextFrame = Rendering.Frame + 1;
            Rendering.Frame = nextFrame;
            Rendering.Tiles = BuildTiles(Rendering.Tiles.Count, nextFrame);

            if (nextFrame % 6 == 0)
            {
                Rendering.FeedItems = Rendering.FeedItems
                    .Select((item, index) => index < 16
                        ? item with
                        {
                            Score = ((item.Score + 7 + index) % 100),
                            ActiveUsers = item.ActiveUsers + (index % 3)
                        }
                        : item)
                    .ToArray();
            }
        }

        NotifyChanged();
    }

    private IReadOnlyList<RenderTile> BuildTiles(int count, int frame)
    {
        var tiles = new List<RenderTile>(count);
        for (var index = 0; index < count; index++)
        {
            var hue = (index * 17 + frame * 5) % 360;
            var scale = 0.92 + ((Math.Sin((frame + index) / 5d) + 1) * 0.18);
            tiles.Add(new RenderTile(
                index + 1,
                $"Node {index + 1}",
                hue,
                scale,
                35 + ((frame * 3 + index * 11) % 65),
                (index + frame) % 9 == 0));
        }

        return tiles;
    }

    private IReadOnlyList<FeedItem> BuildFeedItems(int count)
    {
        var items = new List<FeedItem>(count);
        for (var index = 0; index < count; index++)
        {
            var id = _nextFeedId++;
            items.Add(new FeedItem(
                id,
                $"Rendering lane {id}",
                id % 3 == 0 ? "Hot path" : id % 3 == 1 ? "Scroll" : "Layout",
                12 + ((id * 5) % 140),
                18 + ((id * 11) % 240),
                30 + ((id * 9) % 70),
                $"Card {id} intentionally adds depth, text, and progress indicators for visual-tree analysis."));
        }

        return items;
    }

    private MemoryChunk CreateChunk(int megabytes)
    {
        var blockCount = Math.Max(1, megabytes);
        var buffers = new byte[blockCount][];
        for (var index = 0; index < blockCount; index++)
        {
            var buffer = new byte[1024 * 1024];
            for (var offset = 0; offset < buffer.Length; offset += 4096)
            {
                buffer[offset] = (byte)((index + offset) % 255);
            }

            buffers[index] = buffer;
        }

        var records = Enumerable.Range(0, Math.Max(12, megabytes / 2))
            .Select(index => new RetainedRecord(
                $"payload-{_nextChunkId}-{index}",
                string.Join(' ', Enumerable.Repeat("profiling-sample", 10 + (index % 4))),
                Enumerable.Range(0, 12).Select(number => (number + index + _random.Next(0, 5)) * 1.37d).ToArray()))
            .ToArray();

        var chunk = new MemoryChunk($"chunk-{_nextChunkId++}", buffers, records);
        return chunk;
    }

    private void RefreshMemoryState(string status)
    {
        lock (_memoryGate)
        {
            Memory.ChunkCount = _retainedMemory.Count;
            Memory.RetainedBytes = _retainedMemory.Sum(chunk => chunk.ApproximateBytes);
            Memory.ManagedHeapBytes = GC.GetTotalMemory(false);
            Memory.Status = status;
        }
    }

    private void NotifyChanged()
    {
        Changed?.Invoke();
    }

    public void Dispose()
    {
        StopCpuStress();
        StopMemoryChurn();
        _animationTimer?.Dispose();
    }

    private sealed record RetainedRecord(string Name, string Description, double[] Scores);

    private sealed class MemoryChunk
    {
        public MemoryChunk(string id, byte[][] buffers, RetainedRecord[] records)
        {
            Id = id;
            Buffers = buffers;
            Records = records;
            ApproximateBytes = buffers.Sum(buffer => (long)buffer.Length)
                + records.Sum(record => sizeof(double) * record.Scores.Length + (record.Name.Length + record.Description.Length) * sizeof(char));
        }

        public string Id { get; }

        public byte[][] Buffers { get; }

        public RetainedRecord[] Records { get; }

        public long ApproximateBytes { get; }
    }
}

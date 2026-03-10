using System.Diagnostics;
using MauiSherpa.ProfilingSample.Models;
using MauiSherpa.ProfilingSample.Services;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;

namespace MauiSherpa.ProfilingSample;

public sealed class NativeMainPage : ContentPage, IDisposable
{
    private readonly ProfilingScenarioService _service;
    private IDispatcherTimer? _metricsTimer;
    private IDispatcherTimer? _animationTimer;

    // Metrics labels
    private readonly Label _workingSetLabel = new() { FontSize = 13, FontAttributes = FontAttributes.Bold };
    private readonly Label _privateBytesLabel = new() { FontSize = 13, FontAttributes = FontAttributes.Bold };
    private readonly Label _managedHeapLabel = new() { FontSize = 13, FontAttributes = FontAttributes.Bold };
    private readonly Label _gcGen0Label = new() { FontSize = 13, FontAttributes = FontAttributes.Bold };
    private readonly Label _gcGen1Label = new() { FontSize = 13, FontAttributes = FontAttributes.Bold };
    private readonly Label _gcGen2Label = new() { FontSize = 13, FontAttributes = FontAttributes.Bold };

    // CPU controls
    private readonly Slider _cpuWorkerSlider = new() { Minimum = 1, Maximum = 16, Value = 4 };
    private readonly Slider _cpuDurationSlider = new() { Minimum = 5, Maximum = 180, Value = 30 };
    private readonly Label _cpuWorkerLabel = new() { Text = "4 workers", FontSize = 12 };
    private readonly Label _cpuDurationLabel = new() { Text = "30s", FontSize = 12 };
    private readonly Label _cpuStatusLabel = new() { Text = "Idle", FontSize = 12 };
    private readonly Label _cpuIterationsLabel = new() { Text = "0 iterations", FontSize = 12 };
    private readonly Button _cpuToggleButton = new() { Text = "Start CPU Stress" };

    // Memory controls
    private readonly Label _memoryStatusLabel = new() { FontSize = 12 };
    private readonly Label _memoryRetainedLabel = new() { Text = "Retained: 0 bytes", FontSize = 12 };
    private readonly Label _memoryHeapLabel = new() { Text = "Managed heap: 0 bytes", FontSize = 12 };
    private readonly Label _memoryChunksLabel = new() { Text = "0 chunks", FontSize = 12 };
    private readonly Button _memoryChurnButton = new() { Text = "Start Churn" };

    // Rendering controls
    private readonly Label _renderStatusLabel = new() { Text = "Ready", FontSize = 12 };
    private readonly Label _renderTileCountLabel = new() { Text = "0 tiles", FontSize = 12 };
    private readonly Label _renderFeedCountLabel = new() { Text = "0 feed items", FontSize = 12 };
    private readonly FlexLayout _tileWall = new() { Wrap = FlexWrap.Wrap, AlignContent = FlexAlignContent.Start };
    private readonly VerticalStackLayout _feedList = new() { Spacing = 4 };
    private readonly Button _renderAnimateButton = new() { Text = "Start Animation" };

    // Network controls
    private readonly Slider _networkCountSlider = new() { Minimum = 4, Maximum = 40, Value = 12 };
    private readonly Label _networkCountLabel = new() { Text = "12 requests", FontSize = 12 };
    private readonly Label _networkStatusLabel = new() { Text = "Idle", FontSize = 12 };
    private readonly Label _networkStatsLabel = new() { Text = "", FontSize = 12 };
    private readonly VerticalStackLayout _networkResultsList = new() { Spacing = 2 };

    public NativeMainPage()
    {
        Title = "Profiling Sample (Native)";
        _service = Application.Current!.Handler!.MauiContext!.Services.GetRequiredService<ProfilingScenarioService>();

        var switchButton = new ToolbarItem
        {
            Text = "Blazor View",
            Command = new Command(async () => await Navigation.PushAsync(new BlazorMainPage()))
        };
        ToolbarItems.Add(switchButton);

        BackgroundColor = Color.FromArgb("#0f172a");
        Content = BuildLayout();

        _service.Changed += OnServiceChanged;
        StartMetricsTimer();
    }

    private View BuildLayout()
    {
        return new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Spacing = 16,
                Padding = new Thickness(16, 24, 16, 16),
                Children =
                {
                    BuildHeader(),
                    BuildMetricsStrip(),
                    BuildCpuCard(),
                    BuildMemoryCard(),
                    BuildRenderingCard(),
                    BuildNetworkCard()
                }
            }
        };
    }

    private View BuildHeader()
    {
        return new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                new Label
                {
                    Text = "SHERPA DIAGNOSTICS PLAYGROUND",
                    FontSize = 11,
                    TextColor = Color.FromArgb("#818cf8"),
                    CharacterSpacing = 1.5
                },
                new Label
                {
                    Text = "Profiling Sample — Native MAUI",
                    FontSize = 22,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White
                },
                new Label
                {
                    Text = "CPU, memory, rendering, scrolling, and network-heavy paths using native MAUI controls.",
                    FontSize = 13,
                    TextColor = Color.FromArgb("#94a3b8")
                }
            }
        };
    }

    private View BuildMetricsStrip()
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
            },
            ColumnSpacing = 8,
        };

        grid.Add(MetricCard("Working Set", _workingSetLabel), 0);
        grid.Add(MetricCard("Private Bytes", _privateBytesLabel), 1);
        grid.Add(MetricCard("Managed Heap", _managedHeapLabel), 2);
        grid.Add(MetricCard("GC Gen 0", _gcGen0Label), 3);
        grid.Add(MetricCard("GC Gen 1", _gcGen1Label), 4);
        grid.Add(MetricCard("GC Gen 2", _gcGen2Label), 5);

        return grid;
    }

    private static View MetricCard(string title, Label valueLabel)
    {
        valueLabel.TextColor = Colors.White;
        return new Border
        {
            Stroke = Color.FromArgb("#1e293b"),
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            BackgroundColor = Color.FromArgb("#111827"),
            Padding = new Thickness(10, 8),
            Content = new VerticalStackLayout
            {
                Spacing = 2,
                Children =
                {
                    new Label { Text = title, FontSize = 10, TextColor = Color.FromArgb("#64748b") },
                    valueLabel
                }
            }
        };
    }

    private View BuildCpuCard()
    {
        _cpuWorkerSlider.ValueChanged += (_, e) =>
        {
            var v = (int)e.NewValue;
            _cpuWorkerLabel.Text = $"{v} worker{(v > 1 ? "s" : "")}";
        };
        _cpuDurationSlider.ValueChanged += (_, e) => _cpuDurationLabel.Text = $"{(int)e.NewValue}s";
        _cpuToggleButton.Clicked += OnCpuToggle;

        return ScenarioCard("CPU Stress", "fa-microchip", new View[]
        {
            Row("Workers:", _cpuWorkerSlider, _cpuWorkerLabel),
            Row("Duration:", _cpuDurationSlider, _cpuDurationLabel),
            _cpuToggleButton,
            _cpuStatusLabel,
            _cpuIterationsLabel,
        });
    }

    private View BuildMemoryCard()
    {
        var allocRow = new FlexLayout
        {
            Wrap = FlexWrap.Wrap,
            JustifyContent = FlexJustify.Start,
            Children =
            {
                SmallButton("8 MB", () => _service.AllocateRetainedMemory(8)),
                SmallButton("64 MB", () => _service.AllocateRetainedMemory(64)),
                SmallButton("128 MB", () => _service.AllocateRetainedMemory(128)),
                SmallButton("512 MB", () => _service.AllocateRetainedMemory(512)),
            }
        };

        var actionRow = new FlexLayout
        {
            Wrap = FlexWrap.Wrap,
            JustifyContent = FlexJustify.Start,
            Children =
            {
                SmallButton("Release Half", () => _service.ReleaseHalfMemory()),
                SmallButton("Clear All", () => _service.ClearRetainedMemory()),
                SmallButton("Snapshot", () => _service.CaptureMemorySnapshot()),
            }
        };

        _memoryChurnButton.Clicked += (_, _) =>
        {
            _service.ToggleMemoryChurn();
        };

        return ScenarioCard("Memory", "fa-memory", new View[]
        {
            new Label { Text = "Allocate retained memory:", FontSize = 12, TextColor = Color.FromArgb("#94a3b8") },
            allocRow,
            actionRow,
            _memoryChurnButton,
            _memoryRetainedLabel,
            _memoryHeapLabel,
            _memoryChunksLabel,
            _memoryStatusLabel,
        });
    }

    private View BuildRenderingCard()
    {
        var tileRow = new FlexLayout
        {
            Wrap = FlexWrap.Wrap,
            JustifyContent = FlexJustify.Start,
            Children =
            {
                SmallButton("+60 Tiles", () => _service.AddTiles(60)),
                SmallButton("+180 Tiles", () => _service.AddTiles(180)),
                SmallButton("+480 Tiles", () => _service.AddTiles(480)),
            }
        };

        var feedRow = new FlexLayout
        {
            Wrap = FlexWrap.Wrap,
            JustifyContent = FlexJustify.Start,
            Children =
            {
                SmallButton("+180 Feed", () => _service.AddFeedItems(180)),
                SmallButton("+500 Feed", () => _service.AddFeedItems(500)),
                SmallButton("+1000 Feed", () => _service.AddFeedItems(1000)),
            }
        };

        _renderAnimateButton.Clicked += (_, _) =>
        {
            _service.ToggleRenderingAnimation();
            UpdateRenderingAnimation();
        };

        var resetButton = SmallButton("Reset Scene", () =>
        {
            _service.ResetRenderingScene();
            RebuildTileWall();
            RebuildFeedList();
        });

        var tileScroll = new ScrollView
        {
            HeightRequest = 200,
            Content = _tileWall
        };

        var feedScroll = new ScrollView
        {
            HeightRequest = 300,
            Content = _feedList
        };

        return ScenarioCard("Rendering", "fa-paint-brush", new View[]
        {
            tileRow,
            feedRow,
            new FlexLayout
            {
                Wrap = FlexWrap.Wrap,
                JustifyContent = FlexJustify.Start,
                Children = { _renderAnimateButton, resetButton }
            },
            _renderStatusLabel,
            _renderTileCountLabel,
            _renderFeedCountLabel,
            new Label { Text = "Tile Wall", FontSize = 11, TextColor = Color.FromArgb("#64748b"), Margin = new Thickness(0, 8, 0, 0) },
            tileScroll,
            new Label { Text = "Feed List", FontSize = 11, TextColor = Color.FromArgb("#64748b"), Margin = new Thickness(0, 8, 0, 0) },
            feedScroll,
        });
    }

    private View BuildNetworkCard()
    {
        _networkCountSlider.ValueChanged += (_, e) => _networkCountLabel.Text = $"{(int)e.NewValue} requests";

        var localButton = SmallButton("Local Burst", async () =>
            await _service.RunNativeNetworkBurstAsync("local", (int)_networkCountSlider.Value));
        var remoteButton = SmallButton("Remote Mixed", async () =>
            await _service.RunNativeNetworkBurstAsync("remote-mixed", (int)_networkCountSlider.Value));

        var resultsScroll = new ScrollView
        {
            HeightRequest = 200,
            Content = _networkResultsList
        };

        return ScenarioCard("Network", "fa-wifi", new View[]
        {
            Row("Requests:", _networkCountSlider, _networkCountLabel),
            new FlexLayout
            {
                Wrap = FlexWrap.Wrap,
                JustifyContent = FlexJustify.Start,
                Children = { localButton, remoteButton }
            },
            _networkStatusLabel,
            _networkStatsLabel,
            new Label { Text = "Recent Requests", FontSize = 11, TextColor = Color.FromArgb("#64748b"), Margin = new Thickness(0, 8, 0, 0) },
            resultsScroll,
        });
    }

    // --- Helpers ---

    private static View ScenarioCard(string title, string icon, View[] children)
    {
        var stack = new VerticalStackLayout { Spacing = 8 };
        stack.Add(new Label
        {
            Text = title,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            Margin = new Thickness(0, 0, 0, 4)
        });

        foreach (var child in children)
        {
            if (child is Label lbl)
                lbl.TextColor ??= Color.FromArgb("#cbd5e1");
            stack.Add(child);
        }

        return new Border
        {
            Stroke = Color.FromArgb("#1e293b"),
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            BackgroundColor = Color.FromArgb("#111827"),
            Padding = new Thickness(16),
            Content = stack
        };
    }

    private static View Row(string label, Slider slider, Label valueLabel)
    {
        valueLabel.TextColor = Color.FromArgb("#cbd5e1");
        valueLabel.WidthRequest = 90;
        valueLabel.HorizontalTextAlignment = TextAlignment.End;

        return new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(80)),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(90)),
            },
            ColumnSpacing = 8,
            Children =
            {
                new Label { Text = label, FontSize = 12, TextColor = Color.FromArgb("#94a3b8"), VerticalTextAlignment = TextAlignment.Center }.Column(0),
                slider.Column(1),
                valueLabel.Column(2),
            }
        };
    }

    private static Button SmallButton(string text, Action action)
    {
        var btn = new Button
        {
            Text = text,
            FontSize = 12,
            Padding = new Thickness(12, 6),
            Margin = new Thickness(0, 0, 6, 6),
            BackgroundColor = Color.FromArgb("#334155"),
            TextColor = Colors.White,
            CornerRadius = 6,
            HeightRequest = 34,
        };
        btn.Clicked += (_, _) => action();
        return btn;
    }

    private static Button SmallButton(string text, Func<Task> action)
    {
        var btn = new Button
        {
            Text = text,
            FontSize = 12,
            Padding = new Thickness(12, 6),
            Margin = new Thickness(0, 0, 6, 6),
            BackgroundColor = Color.FromArgb("#334155"),
            TextColor = Colors.White,
            CornerRadius = 6,
            HeightRequest = 34,
        };
        btn.Clicked += async (_, _) => await action();
        return btn;
    }

    // --- State Updates ---

    private void OnServiceChanged()
    {
        Dispatcher.Dispatch(UpdateUi);
    }

    private void UpdateUi()
    {
        // CPU
        _cpuStatusLabel.Text = _service.Cpu.Status;
        _cpuIterationsLabel.Text = $"{_service.Cpu.IterationsCompleted:N0} iterations — {_service.Cpu.LastBatchMilliseconds:F1} ms/batch";
        _cpuToggleButton.Text = _service.Cpu.IsRunning ? "Stop CPU Stress" : "Start CPU Stress";
        _cpuToggleButton.BackgroundColor = _service.Cpu.IsRunning
            ? Color.FromArgb("#ef4444") : Color.FromArgb("#818cf8");

        // Memory
        _memoryRetainedLabel.Text = $"Retained: {FormatBytes(_service.Memory.RetainedBytes)}";
        _memoryHeapLabel.Text = $"Managed heap: {FormatBytes(_service.Memory.ManagedHeapBytes)}";
        _memoryChunksLabel.Text = $"{_service.Memory.ChunkCount} chunk(s)";
        _memoryStatusLabel.Text = _service.Memory.Status;
        _memoryChurnButton.Text = _service.Memory.IsChurning ? "Stop Churn" : "Start Churn";
        _memoryChurnButton.BackgroundColor = _service.Memory.IsChurning
            ? Color.FromArgb("#ef4444") : Color.FromArgb("#334155");

        // Rendering
        _renderStatusLabel.Text = _service.Rendering.Status;
        _renderTileCountLabel.Text = $"{_service.Rendering.Tiles.Count} tiles";
        _renderFeedCountLabel.Text = $"{_service.Rendering.FeedItems.Count} feed items";
        _renderAnimateButton.Text = _service.Rendering.IsAnimating ? "Stop Animation" : "Start Animation";
        _renderAnimateButton.BackgroundColor = _service.Rendering.IsAnimating
            ? Color.FromArgb("#ef4444") : Color.FromArgb("#334155");

        // Network
        _networkStatusLabel.Text = _service.Network.Status;
        if (_service.Network.SuccessCount > 0 || _service.Network.FailureCount > 0)
        {
            _networkStatsLabel.Text = $"✓ {_service.Network.SuccessCount}  ✗ {_service.Network.FailureCount}  " +
                $"Avg: {_service.Network.AverageDurationMs:F0}ms  Total: {FormatBytes(_service.Network.TotalBytes)}";
        }

        RebuildNetworkResults();
    }

    private void UpdateMetrics()
    {
        try
        {
            var process = Process.GetCurrentProcess();
            _workingSetLabel.Text = FormatBytes(process.WorkingSet64);
            _privateBytesLabel.Text = FormatBytes(process.PrivateMemorySize64);
            _managedHeapLabel.Text = FormatBytes(GC.GetTotalMemory(false));
            _gcGen0Label.Text = GC.CollectionCount(0).ToString("N0");
            _gcGen1Label.Text = GC.CollectionCount(1).ToString("N0");
            _gcGen2Label.Text = GC.CollectionCount(2).ToString("N0");
        }
        catch
        {
            // Process info may not be available on all platforms
        }
    }

    private async void OnCpuToggle(object? sender, EventArgs e)
    {
        if (_service.Cpu.IsRunning)
        {
            _service.StopCpuStress();
        }
        else
        {
            await _service.StartCpuStressAsync(
                (int)_cpuWorkerSlider.Value,
                TimeSpan.FromSeconds((int)_cpuDurationSlider.Value));
        }
    }

    private void UpdateRenderingAnimation()
    {
        if (_service.Rendering.IsAnimating)
        {
            _animationTimer ??= Dispatcher.CreateTimer();
            _animationTimer.Interval = TimeSpan.FromMilliseconds(250);
            _animationTimer.Tick += (_, _) =>
            {
                RebuildTileWall();
                if (_service.Rendering.Frame % 6 == 0)
                    RebuildFeedList();
            };
            _animationTimer.Start();
        }
        else
        {
            _animationTimer?.Stop();
        }
    }

    private void RebuildTileWall()
    {
        var tiles = _service.Rendering.Tiles;
        // Only rebuild if count changed significantly to avoid excessive layout
        if (Math.Abs(_tileWall.Children.Count - tiles.Count) > 0 || _service.Rendering.IsAnimating)
        {
            _tileWall.Children.Clear();
            foreach (var tile in tiles.Take(480))
            {
                var box = new BoxView
                {
                    WidthRequest = 28,
                    HeightRequest = 28,
                    Margin = new Thickness(1),
                    Color = Color.FromHsla(tile.Hue / 360.0, 0.7, 0.5),
                    CornerRadius = 4,
                };
                _tileWall.Add(box);
            }
        }
    }

    private void RebuildFeedList()
    {
        var items = _service.Rendering.FeedItems;
        _feedList.Children.Clear();

        foreach (var item in items.Take(200))
        {
            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(new GridLength(60)),
                    new ColumnDefinition(new GridLength(80)),
                },
                ColumnSpacing = 8,
                Padding = new Thickness(8, 4),
                BackgroundColor = Color.FromArgb("#1e293b"),
            };

            row.Add(new Label { Text = item.Title, FontSize = 11, TextColor = Colors.White, LineBreakMode = LineBreakMode.TailTruncation }.Column(0));
            row.Add(new Label { Text = item.Category, FontSize = 10, TextColor = Color.FromArgb("#818cf8"), HorizontalTextAlignment = TextAlignment.Center }.Column(1));

            var progress = new ProgressBar { Progress = item.Score / 100.0, ProgressColor = Color.FromArgb("#818cf8") };
            row.Add(progress.Column(2));

            _feedList.Add(row);
        }
    }

    private void RebuildNetworkResults()
    {
        var results = _service.Network.RecentRequests;
        _networkResultsList.Children.Clear();

        foreach (var result in results)
        {
            var shortUrl = result.Url.Length > 50 ? "..." + result.Url[^47..] : result.Url;
            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(24)),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(new GridLength(60)),
                    new ColumnDefinition(new GridLength(60)),
                },
                ColumnSpacing = 6,
                Padding = new Thickness(6, 3),
                BackgroundColor = Color.FromArgb("#1e293b"),
            };

            var statusColor = result.Success ? Color.FromArgb("#22c55e") : Color.FromArgb("#ef4444");
            row.Add(new Label { Text = result.Success ? "✓" : "✗", TextColor = statusColor, FontSize = 12, HorizontalTextAlignment = TextAlignment.Center }.Column(0));
            row.Add(new Label { Text = shortUrl, FontSize = 10, TextColor = Color.FromArgb("#94a3b8"), LineBreakMode = LineBreakMode.TailTruncation }.Column(1));
            row.Add(new Label { Text = $"{result.DurationMs}ms", FontSize = 10, TextColor = Colors.White, HorizontalTextAlignment = TextAlignment.End }.Column(2));
            row.Add(new Label { Text = FormatBytes(result.Bytes), FontSize = 10, TextColor = Color.FromArgb("#64748b"), HorizontalTextAlignment = TextAlignment.End }.Column(3));

            _networkResultsList.Add(row);
        }
    }

    // --- Timers ---

    private void StartMetricsTimer()
    {
        _metricsTimer = Dispatcher.CreateTimer();
        _metricsTimer.Interval = TimeSpan.FromSeconds(1);
        _metricsTimer.Tick += (_, _) => UpdateMetrics();
        _metricsTimer.Start();
        UpdateMetrics();
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        if (bytes >= 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }

    public void Dispose()
    {
        _service.Changed -= OnServiceChanged;
        _metricsTimer?.Stop();
        _animationTimer?.Stop();
    }
}

internal static class GridExtensions
{
    public static T Column<T>(this T view, int column) where T : View
    {
        Grid.SetColumn(view, column);
        return view;
    }
}

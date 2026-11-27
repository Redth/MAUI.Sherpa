using System.Collections.Concurrent;
using MauiSherpa.Pages.Forms;
using Microsoft.AspNetCore.Components;

namespace MauiSherpa.Core.Tests.Services;

public class HybridFormBridgeTests
{
    [Theory]
    [InlineData("submit")]
    [InlineData("back")]
    [InlineData("next")]
    [InlineData("action")]
    [InlineData("cancel")]
    public async Task NativeRequestsRunOnTheSubscribedRendererContext(string request)
    {
        var context = new TestSynchronizationContext();
        var bridge = new HybridFormBridge();
        var invoked = false;
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            bridge.SubmitRequested += Callback;
            bridge.BackRequested += Callback;
            bridge.NextRequested += Callback;
            bridge.ActionRequested += _ => Callback();
            bridge.CancelRequested += () =>
            {
                Assert.Same(context, SynchronizationContext.Current);
                invoked = true;
                bridge.PreventClose = true;
            };
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        Task Callback()
        {
            Assert.Same(context, SynchronizationContext.Current);
            invoked = true;
            return Task.CompletedTask;
        }

        var pending = request switch
        {
            "submit" => bridge.RequestSubmitAsync(),
            "back" => bridge.RequestBackAsync(),
            "next" => bridge.RequestNextAsync(),
            "action" => bridge.RequestActionAsync("save"),
            "cancel" => bridge.RequestCancelAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(request))
        };

        Assert.False(pending.IsCompleted);
        context.RunNext();
        await pending;
        Assert.True(invoked);
        if (request == "cancel")
            Assert.True(bridge.PreventClose);
    }

    [Fact]
    public async Task CallbackErrorsPropagateToTheNativeCaller()
    {
        var context = new TestSynchronizationContext();
        var bridge = new HybridFormBridge();
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            bridge.SubmitRequested += () => throw new InvalidOperationException("Submit failed");
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        var pending = bridge.RequestSubmitAsync();
        context.RunNext();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
        Assert.Equal("Submit failed", error.Message);
    }

    [Fact]
    public async Task CancelOnlyHandlerRunsOnItsSubscribedContext()
    {
        var context = new TestSynchronizationContext();
        var bridge = new HybridFormBridge();
        var invoked = false;
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            bridge.CancelRequested += () =>
            {
                Assert.Same(context, SynchronizationContext.Current);
                invoked = true;
            };
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        var pending = bridge.RequestCancelAsync();
        Assert.False(pending.IsCompleted);
        context.RunNext();
        await pending;
        Assert.True(invoked);
    }

    [Fact]
    public async Task RendererContextTakesPrecedenceOverNativeCancelSubscription()
    {
        var nativeContext = new TestSynchronizationContext();
        var rendererContext = new TestSynchronizationContext();
        var bridge = new HybridFormBridge();
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(nativeContext);
            bridge.CancelRequested += () => Assert.Same(rendererContext, SynchronizationContext.Current);
            SynchronizationContext.SetSynchronizationContext(rendererContext);
            bridge.SubmitRequested += () => Task.CompletedTask;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        var pending = bridge.RequestCancelAsync();
        Assert.False(nativeContext.HasPendingCallbacks);
        rendererContext.RunNext();
        await pending;
    }

    [Fact]
    public async Task NativeSubmitAwaitsTheBlazorRendererCallback()
    {
        var rendererDispatcher = Dispatcher.CreateDefault();
        var bridge = new HybridFormBridge();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await rendererDispatcher.InvokeAsync(() =>
        {
            bridge.SubmitRequested += async () =>
            {
                rendererDispatcher.AssertAccess();
                started.TrySetResult();
                await release.Task;
                rendererDispatcher.AssertAccess();
            };
        });

        var pending = Task.Run(bridge.RequestSubmitAsync);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(pending.IsCompleted);
        release.TrySetResult();
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RemovedHandlerIsNotInvoked()
    {
        var context = new TestSynchronizationContext();
        var bridge = new HybridFormBridge();
        Func<Task> callback = () => throw new InvalidOperationException("Removed handler ran");
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            bridge.SubmitRequested += callback;
            bridge.SubmitRequested -= callback;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        await bridge.RequestSubmitAsync();
        Assert.False(context.HasPendingCallbacks);
    }

    [Fact]
    public void ActionStateCanDisableAndRestoreTheSecondaryButton()
    {
        var bridge = new HybridFormBridge();
        var states = new List<bool>();
        bridge.ActionStateChanged += () => states.Add(bridge.SecondaryActionEnabled);

        bridge.SetActionState(false, busy: true, secondaryEnabled: false);
        bridge.SetActionState(true);

        Assert.Equal([false, true], states);
    }

    [Fact]
    public void RemovingAClosedParentKeepsTheActiveNestedBridge()
    {
        var holder = new HybridFormBridgeHolder();
        var parent = new HybridFormBridge();
        var nested = new HybridFormBridge();
        holder.Push(parent);
        holder.Push(nested);

        holder.Remove(parent);
        Assert.Same(nested, holder.Current);

        holder.Remove(nested);
        Assert.Null(holder.Current);
    }

    private sealed class TestSynchronizationContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _callbacks = new();

        public bool HasPendingCallbacks => !_callbacks.IsEmpty;

        public override void Post(SendOrPostCallback callback, object? state) =>
            _callbacks.Enqueue((callback, state));

        public void RunNext()
        {
            Assert.True(_callbacks.TryDequeue(out var callback));
            var previous = Current;
            try
            {
                SetSynchronizationContext(this);
                callback.Callback(callback.State);
            }
            finally
            {
                SetSynchronizationContext(previous);
            }
        }
    }
}

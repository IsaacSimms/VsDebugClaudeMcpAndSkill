using VsBridge;

namespace VsBridge.Tests;

// == StaDispatcher Tests == //
// Tests the STA thread dispatcher that marshals work from MTA thread pool
// threads onto a dedicated STA thread, required for COM interop with EnvDTE.
public class StaDispatcherTests : IDisposable
{
    private readonly StaDispatcher _dispatcher = new();

    public void Dispose() => _dispatcher.Dispose();

    // == Invoke returns value from STA thread == //
    // Verifies that Invoke<T> executes the function and returns its result.
    // This is the core contract: caller passes a Func<T>, gets back T.
    [Fact]
    public void Invoke_ReturnsValue()
    {
        var result = _dispatcher.Invoke(() => 42);

        Assert.Equal(42, result);
    }

    // == Work executes on an STA thread == //
    // The whole reason StaDispatcher exists: COM objects like EnvDTE require
    // STA apartment state. This confirms the work actually runs on an STA thread.
    [Fact]
    public void Invoke_RunsOnStaThread()
    {
        var apartmentState = _dispatcher.Invoke(() => Thread.CurrentThread.GetApartmentState());

        Assert.Equal(ApartmentState.STA, apartmentState);
    }

    // == Work runs on a different thread than the caller == //
    // The caller is on a thread pool thread (MTA). StaDispatcher must dispatch
    // to its own dedicated STA thread, not run inline on the caller.
    [Fact]
    public void Invoke_RunsOnDifferentThread()
    {
        int callerThreadId = Thread.CurrentThread.ManagedThreadId;
        int staThreadId = _dispatcher.Invoke(() => Thread.CurrentThread.ManagedThreadId);

        Assert.NotEqual(callerThreadId, staThreadId);
    }

    // == Exceptions propagate back to the caller == //
    // If the work throws on the STA thread, the exception must be re-thrown
    // on the calling thread so error handling works naturally.
    [Fact]
    public void Invoke_PropagatesExceptions()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _dispatcher.Invoke<int>(() => throw new InvalidOperationException("test error")));

        Assert.Equal("test error", ex.Message);
    }

    // == Timeout throws TimeoutException == //
    // If the STA thread is blocked or work takes too long, the caller gets
    // a TimeoutException rather than hanging forever. Uses a short timeout
    // with a long-running task to trigger it.
    [Fact]
    public async Task Invoke_ThrowsOnTimeout()
    {
        // First, block the STA thread with long-running work
        using var blocking = new ManualResetEventSlim(false);
        using var started = new ManualResetEventSlim(false);

        // Queue blocking work on the STA thread
        var blockingTask = Task.Run(() =>
            _dispatcher.Invoke(() =>
            {
                started.Set();
                blocking.Wait(10000); // Block the STA thread
                return 0;
            }));

        started.Wait(5000); // Wait for blocking work to start

        // Now try to invoke with a very short timeout — STA thread is busy
        Assert.Throws<TimeoutException>(() =>
            _dispatcher.Invoke(() => 1, timeoutMs: 100));

        blocking.Set(); // Unblock so cleanup works
        await blockingTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    // == Action overload executes on STA thread == //
    // The void overload (Action instead of Func<T>) must also dispatch
    // to the STA thread and wait for completion.
    [Fact]
    public void Invoke_ActionOverload_Works()
    {
        int captured = 0;
        _dispatcher.Invoke(() => { captured = 99; });

        Assert.Equal(99, captured);
    }

    // == Multiple sequential invocations work == //
    // Ensures the dispatcher doesn't break after a single use. The STA thread
    // processes a queue of work items sequentially.
    [Fact]
    public void Invoke_MultipleSequentialCalls_AllSucceed()
    {
        var results = new List<int>();
        for (int i = 0; i < 10; i++)
        {
            int val = i;
            results.Add(_dispatcher.Invoke(() => val * 2));
        }

        Assert.Equal(Enumerable.Range(0, 10).Select(i => i * 2), results);
    }

    // == Concurrent invocations are serialized on the STA thread == //
    // Multiple callers can submit work simultaneously. The dispatcher
    // serializes all work onto the single STA thread so COM calls are safe.
    [Fact]
    public void Invoke_ConcurrentCallers_AllComplete()
    {
        var tasks = Enumerable.Range(0, 20)
            .Select(i => Task.Run(() => _dispatcher.Invoke(() => i * 3)))
            .ToArray();

        Task.WaitAll(tasks, TimeSpan.FromSeconds(10));

        var results = tasks.Select(t => t.Result).OrderBy(x => x).ToList();
        Assert.Equal(Enumerable.Range(0, 20).Select(i => i * 3), results);
    }

    // == Dispose completes pending work gracefully == //
    // After Dispose, the STA thread should finish any queued work and shut down.
    // New work submitted after dispose should fail (BlockingCollection is completed).
    // Uses a separate dispatcher to avoid double-dispose from xUnit's IDisposable.
    [Fact]
    public void Dispose_PreventsNewWork()
    {
        var separate = new StaDispatcher();
        separate.Dispose();

        Assert.ThrowsAny<Exception>(() => separate.Invoke(() => 1));
    }

    // == STA thread name is set for diagnostics == //
    // The STA thread should be named "VsBridge-STA" so it's identifiable
    // in debugger thread lists and diagnostic tools.
    [Fact]
    public void StaThread_HasExpectedName()
    {
        var name = _dispatcher.Invoke(() => Thread.CurrentThread.Name);

        Assert.Equal("VsBridge-STA", name);
    }
}

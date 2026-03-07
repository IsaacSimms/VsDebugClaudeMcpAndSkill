// <summary>
// Dispatches work to a dedicated STA thread for COM interop with EnvDTE.
// MCP requests arrive on thread-pool (MTA) threads — COM objects require STA.
//
// Important blocks:
//   - RunLoop: consumes queued actions on the STA thread
//   - Invoke<T>: posts work, blocks caller, re-throws exceptions preserving stack trace
//   - Timeout guard: defaults to 15s to prevent deadlocks
// </summary>

using System.Collections.Concurrent;

namespace VsBridge;

// == StaDispatcher == //
public sealed class StaDispatcher : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _staThread;

    // == Constructor — spawns STA thread == //
    public StaDispatcher()
    {
        _staThread = new Thread(RunLoop)
        {
            Name = "VsBridge-STA",
            IsBackground = true
        };
        _staThread.SetApartmentState(ApartmentState.STA);
        _staThread.Start();
    }

    // == RunLoop — STA thread main loop == //
    private void RunLoop()
    {
        foreach (var action in _queue.GetConsumingEnumerable())
        {
            action();
        }
    }

    // == Invoke<T> — execute func on STA thread, return result == //
    public T Invoke<T>(Func<T> func, int timeoutMs = 15000)
    {
        if (Thread.CurrentThread == _staThread)
            return func();                          // Already on STA, run directly

        T result = default!;
        Exception? exception = null;
        using var done = new ManualResetEventSlim(false);

        _queue.Add(() =>
        {
            try
            {
                result = func();
            }
            catch (Exception ex)
            {
                exception = ex;                     // Capture for re-throw on caller thread
            }
            finally
            {
                done.Set();
            }
        });

        if (!done.Wait(timeoutMs))
            throw new TimeoutException($"STA dispatch timed out after {timeoutMs}ms");

        // Re-throw preserving original stack trace
        if (exception is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();

        return result;
    }

    // == Invoke — action overload (no return value) == //
    public void Invoke(Action action, int timeoutMs = 15000)
    {
        Invoke(() => { action(); return 0; }, timeoutMs);
    }

    // == Dispose — signal queue completion, join STA thread == //
    public void Dispose()
    {
        _queue.CompleteAdding();
        _staThread.Join(3000);
        _queue.Dispose();
    }
}

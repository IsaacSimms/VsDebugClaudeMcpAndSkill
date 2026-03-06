using System.Collections.Concurrent;

namespace VsBridge;

/// <summary>
/// Dispatches work to a dedicated STA thread, required for COM interop with EnvDTE.
/// The MCP SDK processes requests on thread pool threads (MTA), but COM objects
/// like EnvDTE require STA apartment state.
/// </summary>
public sealed class StaDispatcher : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _staThread;

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

    private void RunLoop()
    {
        foreach (var action in _queue.GetConsumingEnumerable())
        {
            action();
        }
    }

    /// <summary>
    /// Execute a function on the STA thread and return its result.
    /// </summary>
    public T Invoke<T>(Func<T> func, int timeoutMs = 15000)
    {
        if (Thread.CurrentThread == _staThread)
            return func();

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
                exception = ex;
            }
            finally
            {
                done.Set();
            }
        });

        if (!done.Wait(timeoutMs))
            throw new TimeoutException($"STA dispatch timed out after {timeoutMs}ms");

        if (exception is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();

        return result;
    }

    /// <summary>
    /// Execute an action on the STA thread.
    /// </summary>
    public void Invoke(Action action, int timeoutMs = 15000)
    {
        Invoke(() => { action(); return 0; }, timeoutMs);
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _staThread.Join(3000);
        _queue.Dispose();
    }
}

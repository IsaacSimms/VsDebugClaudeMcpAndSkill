using System.Runtime.InteropServices;
using EnvDTE;
using EnvDTE80;

namespace VsBridge;

/// <summary>
/// Manages the COM connection to a running Visual Studio instance.
/// All COM calls are marshaled through the StaDispatcher.
/// </summary>
public sealed class VsConnection
{
    private readonly StaDispatcher _sta;
    private DTE2? _dte;

    public VsConnection(StaDispatcher sta)
    {
        _sta = sta;
    }

    /// <summary>
    /// Get the DTE2 instance, connecting or reconnecting as needed.
    /// </summary>
    public DTE2 GetDte()
    {
        return _sta.Invoke(() =>
        {
            if (_dte is not null)
            {
                try
                {
                    // Test if the connection is still alive
                    _ = _dte.Version;
                    return _dte;
                }
                catch (COMException)
                {
                    _dte = null; // Stale connection, reconnect
                }
            }

            var instance = ComHelper.FindDteInstance()
                ?? throw new InvalidOperationException(
                    "Visual Studio is not running. Please start VS 2022 or VS 2026 with a solution open.");

            _dte = (DTE2)instance;
            return _dte;
        });
    }

    /// <summary>
    /// Execute a function against the DTE on the STA thread.
    /// </summary>
    public T Execute<T>(Func<DTE2, T> func)
    {
        return _sta.Invoke(() => func(GetDteOnStaThread()));
    }

    /// <summary>
    /// Execute an action against the DTE on the STA thread.
    /// </summary>
    public void Execute(Action<DTE2> action)
    {
        _sta.Invoke(() => action(GetDteOnStaThread()));
    }

    /// <summary>
    /// Throws if the debugger is not in break mode.
    /// Must be called from the STA thread (inside Execute).
    /// </summary>
    public static void EnsureBreakMode(DTE2 dte)
    {
        if (dte.Debugger.CurrentMode != dbgDebugMode.dbgBreakMode)
            throw new InvalidOperationException(
                "Debugger is not paused. The debugger must be in break mode (hit a breakpoint or paused). " +
                "Use vs_set_breakpoint and vs_launch first.");
    }

    /// <summary>
    /// Waits briefly for the debugger to enter break mode after a step command.
    /// Must be called from the STA thread.
    /// </summary>
    public static bool WaitForBreakMode(DTE2 dte, int maxWaitMs = 3000)
    {
        int waited = 0;
        while (waited < maxWaitMs)
        {
            System.Threading.Thread.Sleep(100);
            waited += 100;
            try
            {
                if (dte.Debugger.CurrentMode == dbgDebugMode.dbgBreakMode)
                    return true;
            }
            catch { }
        }
        return false;
    }

    /// <summary>
    /// Gets the current source location string if in break mode.
    /// Must be called from the STA thread.
    /// </summary>
    public static string GetCurrentLocation(DTE2 dte)
    {
        try
        {
            if (dte.Debugger.CurrentMode != dbgDebugMode.dbgBreakMode)
                return "";

            var frame = dte.Debugger.CurrentStackFrame;
            if (frame is null) return "";

            string func = frame.FunctionName;
            string file = "";
            int line = 0;

            try
            {
                file = dte.Debugger.CurrentProgram?.Name ?? "";
                // Try to get file/line from the current stack frame document context
                var doc = dte.ActiveDocument;
                if (doc is not null)
                {
                    file = doc.FullName;
                    var sel = doc.Selection as TextSelection;
                    if (sel is not null)
                        line = sel.ActivePoint.Line;
                }
            }
            catch { }

            return line > 0 ? $"{file}:{line} in {func}" : func;
        }
        catch
        {
            return "";
        }
    }

    private DTE2 GetDteOnStaThread()
    {
        if (_dte is not null)
        {
            try
            {
                _ = _dte.Version;
                return _dte;
            }
            catch (COMException)
            {
                _dte = null;
            }
        }

        var instance = ComHelper.FindDteInstance()
            ?? throw new InvalidOperationException(
                "Visual Studio is not running. Please start VS 2022 or VS 2026 with a solution open.");

        _dte = (DTE2)instance;
        return _dte;
    }
}

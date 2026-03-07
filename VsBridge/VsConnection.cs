// <summary>
// Manages the long-lived COM connection to a running Visual Studio instance.
// All DTE calls are marshaled through StaDispatcher.
//
// Important blocks:
//   - GetDte / GetDteOnStaThread: lazy connect with stale-connection recovery
//   - Execute: thread-safe wrapper used by all MCP tools
//   - EnsureBreakMode: guard — throws if debugger isn't paused
//   - WaitForBreakMode: polls 100ms intervals (up to 3s) after step commands
//   - GetCurrentLocation: extracts file:line from active document / stack frame
// </summary>

using System.Runtime.InteropServices;
using EnvDTE;
using EnvDTE80;

namespace VsBridge;

// == VsConnection — DTE2 lifecycle and thread-safe execution == //
public sealed class VsConnection
{
    private readonly StaDispatcher _sta;
    private DTE2? _dte;

    public VsConnection(StaDispatcher sta)
    {
        _sta = sta;
    }

    // == GetDte — connect or reconnect on STA thread == //
    public DTE2 GetDte()
    {
        return _sta.Invoke(() =>
        {
            if (_dte is not null)
            {
                try
                {
                    _ = _dte.Version;               // Liveness check
                    return _dte;
                }
                catch (COMException)
                {
                    _dte = null;                    // Stale connection, reconnect
                }
            }

            var instance = ComHelper.FindDteInstance()
                ?? throw new InvalidOperationException(
                    "Visual Studio is not running. Please start VS 2022 or VS 2026 with a solution open.");

            _dte = (DTE2)instance;
            return _dte;
        });
    }

    // == Execute<T> — run func against DTE on STA thread == //
    public T Execute<T>(Func<DTE2, T> func)
    {
        return _sta.Invoke(() => func(GetDteOnStaThread()));
    }

    // == Execute — action overload (no return value) == //
    public void Execute(Action<DTE2> action)
    {
        _sta.Invoke(() => action(GetDteOnStaThread()));
    }

    // == EnsureBreakMode — guard, throws if debugger not paused == //
    public static void EnsureBreakMode(DTE2 dte)
    {
        if (dte.Debugger.CurrentMode != dbgDebugMode.dbgBreakMode)
            throw new InvalidOperationException(
                "Debugger is not paused. The debugger must be in break mode (hit a breakpoint or paused). " +
                "Use vs_set_breakpoint and vs_launch first.");
    }

    // == WaitForBreakMode — polls until debugger settles into break mode == //
    public static bool WaitForBreakMode(DTE2 dte, int maxWaitMs = 3000)
    {
        int waited = 0;
        while (waited < maxWaitMs)
        {
            System.Threading.Thread.Sleep(100);     // 100ms polling interval
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

    // == GetCurrentLocation — file:line in func string from active context == //
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
                // Try to resolve file/line from the active document context
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

    // == GetDteOnStaThread — private reconnect (assumes already on STA) == //
    private DTE2 GetDteOnStaThread()
    {
        if (_dte is not null)
        {
            try
            {
                _ = _dte.Version;                   // Liveness check
                return _dte;
            }
            catch (COMException)
            {
                _dte = null;                        // Stale, reconnect
            }
        }

        var instance = ComHelper.FindDteInstance()
            ?? throw new InvalidOperationException(
                "Visual Studio is not running. Please start VS 2022 or VS 2026 with a solution open.");

        _dte = (DTE2)instance;
        return _dte;
    }
}

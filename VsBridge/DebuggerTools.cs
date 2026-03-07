// <summary>
// MCP tool definitions — exposes VS debugger operations to MCP clients.
// Each static method is a tool callable over JSON-RPC via VsConnection.Execute.
// All tools return error strings on failure (never throw).
//
// Tools:
//   - vs_status:            debugger mode, solution, breakpoints
//   - vs_launch:            start/resume debugging (F5)
//   - vs_stop:              stop debugging (Shift+F5)
//   - vs_set_breakpoint:    set breakpoint at file:line, optional condition
//   - vs_remove_breakpoint: remove breakpoint at file:line
//   - vs_get_locals:        local variables at current stack frame
//   - vs_evaluate:          evaluate C# expression in debug context
//   - vs_step_over:         F10
//   - vs_step_into:         F11
//   - vs_step_out:          Shift+F11
//   - vs_get_callstack:     full call stack with module info
// </summary>

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using EnvDTE;
using EnvDTE80;
using ModelContextProtocol.Server;

namespace VsBridge;

// == DebuggerTools — MCP tool surface area == //
[McpServerToolType]
public sealed class DebuggerTools
{
    // == vs_status — report debugger state, solution, breakpoints == //
    [McpServerTool(Name = "vs_status"), Description(
        "Check the current state of the Visual Studio debugger. Returns whether VS is in design mode " +
        "(not debugging), run mode (running), or break mode (paused at breakpoint/step). Also returns " +
        "the solution name and current source location when in break mode. Call this first before using " +
        "other debugger tools to understand the current state.")]
    public static string Status(VsConnection vs)
    {
        try
        {
            return vs.Execute(dte =>
            {
                var mode = dte.Debugger.CurrentMode switch
                {
                    dbgDebugMode.dbgDesignMode => "Design (not debugging)",
                    dbgDebugMode.dbgRunMode    => "Running",
                    dbgDebugMode.dbgBreakMode  => "Break (paused)",
                    _                          => "Unknown"
                };

                var sb = new StringBuilder();
                sb.AppendLine($"Debugger Mode: {mode}");
                sb.AppendLine($"Solution: {dte.Solution.FullName}");

                if (dte.Debugger.CurrentMode == dbgDebugMode.dbgBreakMode)
                {
                    var location = VsConnection.GetCurrentLocation(dte);
                    if (!string.IsNullOrEmpty(location))
                        sb.AppendLine($"Current Location: {location}");
                }

                // List active breakpoints
                var bps = dte.Debugger.Breakpoints;
                if (bps.Count > 0)
                {
                    sb.AppendLine($"Breakpoints ({bps.Count}):");
                    foreach (Breakpoint bp in bps)
                    {
                        string enabled = bp.Enabled ? "enabled" : "disabled";
                        sb.AppendLine($"  {bp.File}:{bp.FileLine} ({enabled})");
                    }
                }

                return sb.ToString().TrimEnd();
            });
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    // == vs_launch — start debugging or resume from break == //
    [McpServerTool(Name = "vs_launch"), Description(
        "Start debugging the current project in Visual Studio (equivalent to pressing F5). " +
        "The startup project must be set in the solution. If breakpoints are set, execution will " +
        "pause when it hits one. Use vs_set_breakpoint before calling this if you want to pause " +
        "at a specific location.")]
    public static string Launch(VsConnection vs)
    {
        try
        {
            return vs.Execute(dte =>
            {
                if (dte.Debugger.CurrentMode == dbgDebugMode.dbgRunMode)
                    return "Debugger is already running.";

                if (dte.Debugger.CurrentMode == dbgDebugMode.dbgBreakMode)
                {
                    dte.Debugger.Go(false);         // Resume from break
                    return "Resumed execution from break mode.";
                }

                dte.Debugger.Go(false);             // Start fresh (F5)

                // Wait a bit for a breakpoint to hit so we can report the location
                if (VsConnection.WaitForBreakMode(dte, maxWaitMs: 5000))
                {
                    var location = VsConnection.GetCurrentLocation(dte);
                    return $"Debugging started. Hit breakpoint.{(string.IsNullOrEmpty(location) ? "" : $" Paused at: {location}")}";
                }

                return "Debugging started (F5). Running — use vs_status to check state.";
            });
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    // == vs_stop — stop debugging session == //
    [McpServerTool(Name = "vs_stop"), Description(
        "Stop the current debugging session in Visual Studio (equivalent to Shift+F5). " +
        "Use this when you're done debugging or want to restart.")]
    public static string Stop(VsConnection vs)
    {
        try
        {
            return vs.Execute(dte =>
            {
                if (dte.Debugger.CurrentMode == dbgDebugMode.dbgDesignMode)
                    return "Debugger is not running.";

                dte.Debugger.Stop(false);
                return "Debugging stopped.";
            });
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    // == vs_set_breakpoint — set breakpoint at file:line with optional condition == //
    [McpServerTool(Name = "vs_set_breakpoint"), Description(
        "Set a breakpoint in Visual Studio at a specific file and line number. The file can be " +
        "a full path (e.g. C:\\Projects\\MyApp\\MyService.cs) or just the filename (e.g. MyService.cs) " +
        "if it's unique in the solution. Optionally set a condition expression that must be true " +
        "for the breakpoint to hit.")]
    public static string SetBreakpoint(
        VsConnection vs,
        [Description("File path or filename where the breakpoint should be set")] string file,
        [Description("Line number (1-based) where the breakpoint should be set")] int line,
        [Description("Optional condition expression (e.g. 'x > 5'). Breakpoint only hits when this is true.")] string? condition = null)
    {
        try
        {
            return vs.Execute(dte =>
            {
                if (!string.IsNullOrEmpty(condition))
                {
                    dte.Debugger.Breakpoints.Add(
                        File: file, Line: line,
                        Condition: condition,
                        ConditionType: dbgBreakpointConditionType.dbgBreakpointConditionTypeWhenTrue);
                }
                else
                {
                    dte.Debugger.Breakpoints.Add(File: file, Line: line);
                }

                return $"Breakpoint set at {file}:{line}" +
                    (string.IsNullOrEmpty(condition) ? "" : $" (condition: {condition})");
            });
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    // == vs_remove_breakpoint — remove breakpoint at file:line == //
    [McpServerTool(Name = "vs_remove_breakpoint"), Description(
        "Remove a breakpoint at a specific file and line number in Visual Studio.")]
    public static string RemoveBreakpoint(
        VsConnection vs,
        [Description("File path or filename of the breakpoint to remove")] string file,
        [Description("Line number of the breakpoint to remove")] int line)
    {
        try
        {
            return vs.Execute(dte =>
            {
                int removed = 0;
                // Reverse iterate to safely delete without index shifting
                for (int i = dte.Debugger.Breakpoints.Count; i >= 1; i--)
                {
                    var bp = dte.Debugger.Breakpoints.Item(i);
                    if (bp.FileLine == line &&
                        (bp.File.Equals(file, StringComparison.OrdinalIgnoreCase) ||
                         bp.File.EndsWith("\\" + file, StringComparison.OrdinalIgnoreCase) ||
                         bp.File.EndsWith("/" + file, StringComparison.OrdinalIgnoreCase)))
                    {
                        bp.Delete();
                        removed++;
                    }
                }

                return removed > 0
                    ? $"Removed {removed} breakpoint(s) at {file}:{line}"
                    : $"No breakpoint found at {file}:{line}";
            });
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    // == vs_get_locals — local variables at current execution point == //
    [McpServerTool(Name = "vs_get_locals"), Description(
        "Get the local variables and their values at the current execution point. Only works when " +
        "the debugger is paused (break mode). Returns variable names, types, and current values. " +
        "Use this to inspect program state after hitting a breakpoint or stepping.")]
    public static string GetLocals(VsConnection vs)
    {
        try
        {
            return vs.Execute(dte =>
            {
                VsConnection.EnsureBreakMode(dte);

                var frame = dte.Debugger.CurrentStackFrame;
                if (frame is null)
                    return "Error: No current stack frame available.";

                var sb = new StringBuilder();
                var location = VsConnection.GetCurrentLocation(dte);
                if (!string.IsNullOrEmpty(location))
                    sb.AppendLine($"Location: {location}");
                sb.AppendLine();

                foreach (Expression local in frame.Locals)
                {
                    AppendExpression(sb, local, indent: 0, maxDepth: 2);
                }

                return sb.ToString().TrimEnd();
            });
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    // == vs_evaluate — evaluate C# expression in debug context == //
    [McpServerTool(Name = "vs_evaluate"), Description(
        "Evaluate a C# expression in the current debugging context. Only works when the debugger " +
        "is paused (break mode). Can evaluate variables, properties, method calls, LINQ expressions, " +
        "etc. Examples: 'myVariable', 'myList.Count', 'customer.Name', 'x + y', 'items.Where(i => i > 5).Count()'.")]
    public static string Evaluate(
        VsConnection vs,
        [Description("The C# expression to evaluate in the current debug context")] string expression)
    {
        try
        {
            return vs.Execute(dte =>
            {
                VsConnection.EnsureBreakMode(dte);

                var result = dte.Debugger.GetExpression(expression);
                if (!result.IsValidValue)
                    return $"Could not evaluate '{expression}': {result.Value}";

                return $"{expression} = {result.Value} (Type: {result.Type})";
            });
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    // == vs_step_over — F10 == //
    [McpServerTool(Name = "vs_step_over"), Description(
        "Step over the current line of code (equivalent to F10). Executes the current line and " +
        "pauses at the next line in the same method. Only works when the debugger is paused.")]
    public static string StepOver(VsConnection vs)
    {
        try
        {
            return vs.Execute(dte =>
            {
                VsConnection.EnsureBreakMode(dte);
                dte.Debugger.StepOver(false);
                VsConnection.WaitForBreakMode(dte);
                var location = VsConnection.GetCurrentLocation(dte);
                return $"Stepped over.{(string.IsNullOrEmpty(location) ? "" : $" Now at: {location}")}";
            });
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    // == vs_step_into — F11 == //
    [McpServerTool(Name = "vs_step_into"), Description(
        "Step into the current method call (equivalent to F11). If the current line contains a " +
        "method call, enters that method. Only works when the debugger is paused.")]
    public static string StepInto(VsConnection vs)
    {
        try
        {
            return vs.Execute(dte =>
            {
                VsConnection.EnsureBreakMode(dte);
                dte.Debugger.StepInto(false);
                VsConnection.WaitForBreakMode(dte);
                var location = VsConnection.GetCurrentLocation(dte);
                return $"Stepped into.{(string.IsNullOrEmpty(location) ? "" : $" Now at: {location}")}";
            });
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    // == vs_step_out — Shift+F11 == //
    [McpServerTool(Name = "vs_step_out"), Description(
        "Step out of the current method (equivalent to Shift+F11). Continues execution until the " +
        "current method returns, then pauses at the calling line. Only works when the debugger is paused.")]
    public static string StepOut(VsConnection vs)
    {
        try
        {
            return vs.Execute(dte =>
            {
                VsConnection.EnsureBreakMode(dte);
                dte.Debugger.StepOut(false);
                VsConnection.WaitForBreakMode(dte);
                var location = VsConnection.GetCurrentLocation(dte);
                return $"Stepped out.{(string.IsNullOrEmpty(location) ? "" : $" Now at: {location}")}";
            });
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    // == vs_get_callstack — full call stack with module info == //
    [McpServerTool(Name = "vs_get_callstack"), Description(
        "Get the current call stack showing the chain of method calls that led to the current " +
        "execution point. Only works when the debugger is paused (break mode). Shows function names, " +
        "file paths, and line numbers for each frame.")]
    public static string GetCallstack(VsConnection vs)
    {
        try
        {
            return vs.Execute(dte =>
            {
                VsConnection.EnsureBreakMode(dte);

                var thread = dte.Debugger.CurrentThread;
                if (thread is null)
                    return "Error: No current thread.";

                var sb = new StringBuilder();
                sb.AppendLine("Call Stack:");

                int i = 0;
                foreach (StackFrame frame in thread.StackFrames)
                {
                    string marker = i == 0 ? " >> " : "    "; // >> marks current frame
                    sb.AppendLine($"{marker}{frame.FunctionName}");
                    try
                    {
                        string module = frame.Module;
                        if (!string.IsNullOrEmpty(module))
                            sb.AppendLine($"       Module: {module}");
                    }
                    catch { }
                    i++;
                }

                return sb.ToString().TrimEnd();
            });
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    // == AppendExpression — recursively format Expression tree for locals == //
    private static void AppendExpression(StringBuilder sb, Expression expr, int indent, int maxDepth)
    {
        string pad = new(' ', indent * 2);

        if (expr.DataMembers.Count > 0 && indent < maxDepth)
        {
            sb.AppendLine($"{pad}{expr.Name} ({expr.Type}) = {expr.Value}");
            foreach (Expression member in expr.DataMembers)
            {
                AppendExpression(sb, member, indent + 1, maxDepth);
            }
        }
        else
        {
            sb.AppendLine($"{pad}{expr.Name} ({expr.Type}) = {expr.Value}");
        }
    }
}

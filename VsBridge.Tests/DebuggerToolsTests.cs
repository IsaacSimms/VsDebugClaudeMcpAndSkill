using System.Reflection;
using EnvDTE;
using EnvDTE80;
using Moq;
using VsBridge;

namespace VsBridge.Tests;

// == DebuggerTools Tests == //
// Tests the MCP tool methods in DebuggerTools. Each tool is a static method
// that takes a VsConnection and returns a string. Since these call into COM
// via VsConnection.Execute, we inject a mock DTE2 into VsConnection's private
// _dte field using reflection, bypassing real COM discovery.
public class DebuggerToolsTests : IDisposable
{
    private readonly StaDispatcher _sta = new();

    public void Dispose() => _sta.Dispose();

    // == Status reports design mode correctly == //
    // The vs_status tool should describe the current debugger state. When in
    // design mode (not debugging), it reports "Design (not debugging)".
    [Fact]
    public void Status_ReportsDesignMode()
    {
        var debugger = CreateMockDebugger(dbgDebugMode.dbgDesignMode);

        var mockBreakpoints = new Mock<Breakpoints>();
        mockBreakpoints.Setup(b => b.Count).Returns(0);
        debugger.Setup(d => d.Breakpoints).Returns(mockBreakpoints.Object);

        var vs = CreateVsConnection(debugger.Object, mock =>
        {
            mock.Setup(d => d.Solution.FullName).Returns(@"C:\test\MySolution.sln");
        });

        var result = DebuggerTools.Status(vs);

        Assert.Contains("Design (not debugging)", result);
        Assert.Contains("MySolution.sln", result);
    }

    // == Status reports break mode correctly == //
    // When paused at a breakpoint, vs_status should report "Break (paused)"
    // and include the current source location.
    [Fact]
    public void Status_ReportsBreakMode()
    {
        var debugger = CreateMockDebugger(dbgDebugMode.dbgBreakMode);

        var mockBreakpoints = new Mock<Breakpoints>();
        mockBreakpoints.Setup(b => b.Count).Returns(0);
        debugger.Setup(d => d.Breakpoints).Returns(mockBreakpoints.Object);

        var mockFrame = new Mock<StackFrame>();
        mockFrame.Setup(f => f.FunctionName).Returns("Program.Main");
        debugger.Setup(d => d.CurrentStackFrame).Returns(mockFrame.Object);
        debugger.Setup(d => d.CurrentProgram).Returns((Program)null!);

        var vs = CreateVsConnection(debugger.Object, mock =>
        {
            mock.Setup(d => d.Solution.FullName).Returns(@"C:\test\MySolution.sln");
            mock.Setup(d => d.ActiveDocument).Returns((Document)null!);
        });

        var result = DebuggerTools.Status(vs);

        Assert.Contains("Break (paused)", result);
    }

    // == Launch returns already-running message == //
    // If the debugger is already in run mode, vs_launch should not start
    // another session — it returns a helpful message instead.
    [Fact]
    public void Launch_ReturnsAlreadyRunning_WhenRunMode()
    {
        var debugger = CreateMockDebugger(dbgDebugMode.dbgRunMode);
        var vs = CreateVsConnection(debugger.Object);

        var result = DebuggerTools.Launch(vs);

        Assert.Equal("Debugger is already running.", result);
    }

    // == Launch resumes from break mode == //
    // If already paused (break mode), vs_launch resumes execution
    // rather than starting a new session.
    [Fact]
    public void Launch_ResumesFromBreakMode()
    {
        var debugger = CreateMockDebugger(dbgDebugMode.dbgBreakMode);
        var vs = CreateVsConnection(debugger.Object);

        var result = DebuggerTools.Launch(vs);

        Assert.Equal("Resumed execution from break mode.", result);
        debugger.Verify(d => d.Go(false), Times.Once);
    }

    // == Stop returns not-running message when in design mode == //
    // Calling vs_stop when not debugging should inform the user rather
    // than throwing an error.
    [Fact]
    public void Stop_ReturnsNotRunning_WhenDesignMode()
    {
        var debugger = CreateMockDebugger(dbgDebugMode.dbgDesignMode);
        var vs = CreateVsConnection(debugger.Object);

        var result = DebuggerTools.Stop(vs);

        Assert.Equal("Debugger is not running.", result);
    }

    // == Stop stops the debugger when running == //
    // When the debugger is in run mode, vs_stop should call
    // Debugger.Stop and return a confirmation message.
    [Fact]
    public void Stop_StopsDebugger_WhenRunning()
    {
        var debugger = CreateMockDebugger(dbgDebugMode.dbgRunMode);
        var vs = CreateVsConnection(debugger.Object);

        var result = DebuggerTools.Stop(vs);

        Assert.Equal("Debugging stopped.", result);
        debugger.Verify(d => d.Stop(false), Times.Once);
    }

    // == SetBreakpoint returns confirmation message == //
    // vs_set_breakpoint should call Debugger.Breakpoints.Add and return
    // a message confirming the file and line.
    [Fact]
    public void SetBreakpoint_ReturnsConfirmation()
    {
        var debugger = CreateMockDebugger(dbgDebugMode.dbgDesignMode);
        var mockBreakpoints = new Mock<Breakpoints>();
        debugger.Setup(d => d.Breakpoints).Returns(mockBreakpoints.Object);

        var vs = CreateVsConnection(debugger.Object);

        var result = DebuggerTools.SetBreakpoint(vs, "MyFile.cs", 42);

        Assert.Equal("Breakpoint set at MyFile.cs:42", result);
    }

    // == SetBreakpoint with condition includes condition in message == //
    // Conditional breakpoints should report the condition expression
    // so the user can verify it was set correctly.
    [Fact]
    public void SetBreakpoint_WithCondition_IncludesConditionInMessage()
    {
        var debugger = CreateMockDebugger(dbgDebugMode.dbgDesignMode);
        var mockBreakpoints = new Mock<Breakpoints>();
        debugger.Setup(d => d.Breakpoints).Returns(mockBreakpoints.Object);

        var vs = CreateVsConnection(debugger.Object);

        var result = DebuggerTools.SetBreakpoint(vs, "MyFile.cs", 42, condition: "x > 5");

        Assert.Contains("condition: x > 5", result);
        Assert.Contains("MyFile.cs:42", result);
    }

    // == RemoveBreakpoint reports no breakpoint found == //
    // When no breakpoints match the file/line, the tool should report
    // "No breakpoint found" rather than throwing or silently succeeding.
    [Fact]
    public void RemoveBreakpoint_ReportsNotFound_WhenNoMatch()
    {
        var debugger = CreateMockDebugger(dbgDebugMode.dbgDesignMode);
        var mockBreakpoints = new Mock<Breakpoints>();
        mockBreakpoints.Setup(b => b.Count).Returns(0);
        debugger.Setup(d => d.Breakpoints).Returns(mockBreakpoints.Object);

        var vs = CreateVsConnection(debugger.Object);

        var result = DebuggerTools.RemoveBreakpoint(vs, "MyFile.cs", 42);

        Assert.Contains("No breakpoint found", result);
    }

    // == Evaluate returns error when not in break mode == //
    // vs_evaluate requires break mode. When not paused, the tool should
    // return an error string (not throw) so the MCP layer gets a usable response.
    [Fact]
    public void Evaluate_ReturnsError_WhenNotBreakMode()
    {
        var debugger = CreateMockDebugger(dbgDebugMode.dbgDesignMode);
        var vs = CreateVsConnection(debugger.Object);

        var result = DebuggerTools.Evaluate(vs, "myVar");

        Assert.StartsWith("Error:", result);
    }

    // == GetLocals returns error when not in break mode == //
    [Fact]
    public void GetLocals_ReturnsError_WhenNotBreakMode()
    {
        var debugger = CreateMockDebugger(dbgDebugMode.dbgDesignMode);
        var vs = CreateVsConnection(debugger.Object);

        var result = DebuggerTools.GetLocals(vs);

        Assert.StartsWith("Error:", result);
    }

    // == Step tools return error when not in break mode == //
    // StepOver, StepInto, StepOut all require break mode. Each should
    // return an error string when the debugger isn't paused.
    [Theory]
    [InlineData("StepOver")]
    [InlineData("StepInto")]
    [InlineData("StepOut")]
    public void StepTools_ReturnError_WhenNotBreakMode(string toolName)
    {
        var debugger = CreateMockDebugger(dbgDebugMode.dbgDesignMode);
        var vs = CreateVsConnection(debugger.Object);

        var result = toolName switch
        {
            "StepOver" => DebuggerTools.StepOver(vs),
            "StepInto" => DebuggerTools.StepInto(vs),
            "StepOut"  => DebuggerTools.StepOut(vs),
            _ => throw new ArgumentException(toolName)
        };

        Assert.StartsWith("Error:", result);
    }

    // == GetCallstack returns error when not in break mode == //
    [Fact]
    public void GetCallstack_ReturnsError_WhenNotBreakMode()
    {
        var debugger = CreateMockDebugger(dbgDebugMode.dbgDesignMode);
        var vs = CreateVsConnection(debugger.Object);

        var result = DebuggerTools.GetCallstack(vs);

        Assert.StartsWith("Error:", result);
    }

    // == All tools catch exceptions and return error strings == //
    // This is a key design constraint: MCP tools return error strings so
    // the MCP layer always gets a usable response, even on failure.
    // We inject a mock DTE that throws on every Debugger access to simulate
    // COM failures, then verify every tool returns "Error:" instead of throwing.
    [Fact]
    public void AllTools_ReturnErrorStrings_NeverThrow()
    {
        var mockDebugger = new Mock<Debugger>();
        mockDebugger.Setup(d => d.CurrentMode).Throws(new System.Runtime.InteropServices.COMException("COM failure"));

        var vs = CreateVsConnection(mockDebugger.Object);

        var results = new[]
        {
            DebuggerTools.Status(vs),
            DebuggerTools.Launch(vs),
            DebuggerTools.Stop(vs),
            DebuggerTools.Evaluate(vs, "x"),
            DebuggerTools.GetLocals(vs),
            DebuggerTools.StepOver(vs),
            DebuggerTools.StepInto(vs),
            DebuggerTools.StepOut(vs),
            DebuggerTools.GetCallstack(vs),
            DebuggerTools.SetBreakpoint(vs, "file.cs", 1),
            DebuggerTools.RemoveBreakpoint(vs, "file.cs", 1),
        };

        // None should throw, all should start with "Error:"
        foreach (var result in results)
        {
            Assert.StartsWith("Error:", result);
        }
    }

    // == Helper: create a mock Debugger with a specific mode == //
    // Uses Mock<Debugger> (not Debugger2) because DTE2.Debugger returns
    // the base Debugger type. All methods used by DebuggerTools are on Debugger.
    private static Mock<Debugger> CreateMockDebugger(dbgDebugMode mode)
    {
        var mock = new Mock<Debugger>();
        mock.Setup(d => d.CurrentMode).Returns(mode);
        return mock;
    }

    // == Helper: create a VsConnection with a mock DTE2 injected == //
    // Uses reflection to set the private _dte field, bypassing COM discovery.
    private VsConnection CreateVsConnection(Debugger debugger, Action<Mock<DTE2>>? configureMock = null)
    {
        var mock = new Mock<DTE2>();
        mock.Setup(d => d.Version).Returns("17.0");  // Connection liveness check
        mock.Setup(d => d.Debugger).Returns(debugger);
        configureMock?.Invoke(mock);

        var vs = new VsConnection(_sta);

        // Inject mock DTE into the private _dte field via reflection
        var dteField = typeof(VsConnection).GetField("_dte", BindingFlags.NonPublic | BindingFlags.Instance)!;
        _sta.Invoke(() => dteField.SetValue(vs, mock.Object));

        return vs;
    }
}

using EnvDTE;
using EnvDTE80;
using Moq;
using VsBridge;

namespace VsBridge.Tests;

// == VsConnection Tests == //
// Tests the VsConnection class which manages the DTE2 COM connection and
// provides helper methods for debugger state checks. Since actual COM/DTE
// requires a running Visual Studio, we mock the DTE2 interface.
public class VsConnectionTests : IDisposable
{
    private readonly StaDispatcher _sta = new();

    public void Dispose() => _sta.Dispose();

    // == EnsureBreakMode throws when in design mode == //
    // Many debugger tools require break mode (paused at breakpoint).
    // EnsureBreakMode guards against calling them when not debugging.
    [Fact]
    public void EnsureBreakMode_ThrowsWhenDesignMode()
    {
        var dte = CreateMockDte(dbgDebugMode.dbgDesignMode);

        var ex = Assert.Throws<InvalidOperationException>(
            () => VsConnection.EnsureBreakMode(dte));

        Assert.Contains("not paused", ex.Message);
    }

    // == EnsureBreakMode throws when in run mode == //
    // When the app is running (not paused), debugger queries like
    // GetLocals or Evaluate would fail. EnsureBreakMode catches this.
    [Fact]
    public void EnsureBreakMode_ThrowsWhenRunMode()
    {
        var dte = CreateMockDte(dbgDebugMode.dbgRunMode);

        Assert.Throws<InvalidOperationException>(
            () => VsConnection.EnsureBreakMode(dte));
    }

    // == EnsureBreakMode succeeds in break mode == //
    // When the debugger is paused (break mode), the guard should pass
    // without throwing, allowing the tool to proceed.
    [Fact]
    public void EnsureBreakMode_SucceedsInBreakMode()
    {
        var dte = CreateMockDte(dbgDebugMode.dbgBreakMode);

        var exception = Record.Exception(() => VsConnection.EnsureBreakMode(dte));

        Assert.Null(exception);
    }

    // == GetCurrentLocation returns empty when not in break mode == //
    // When not paused, there's no "current location" to report.
    // The method returns empty string rather than throwing.
    [Fact]
    public void GetCurrentLocation_ReturnsEmpty_WhenNotBreakMode()
    {
        var dte = CreateMockDte(dbgDebugMode.dbgDesignMode);

        var result = VsConnection.GetCurrentLocation(dte);

        Assert.Equal("", result);
    }

    // == GetCurrentLocation returns function name in break mode == //
    // When paused, it should return at least the function name from
    // the current stack frame. If file/line info isn't available,
    // the function name alone is returned.
    [Fact]
    public void GetCurrentLocation_ReturnsFunctionName_WhenBreakMode()
    {
        var mockDebugger = new Mock<Debugger>();
        mockDebugger.Setup(d => d.CurrentMode).Returns(dbgDebugMode.dbgBreakMode);

        var mockFrame = new Mock<StackFrame>();
        mockFrame.Setup(f => f.FunctionName).Returns("MyClass.MyMethod");
        mockDebugger.Setup(d => d.CurrentStackFrame).Returns(mockFrame.Object);
        mockDebugger.Setup(d => d.CurrentProgram).Returns((Program)null!);

        var mockDte = new Mock<DTE2>();
        mockDte.Setup(d => d.Debugger).Returns(mockDebugger.Object);
        mockDte.Setup(d => d.ActiveDocument).Returns((Document)null!);

        var result = VsConnection.GetCurrentLocation(mockDte.Object);

        Assert.Equal("MyClass.MyMethod", result);
    }

    // == WaitForBreakMode returns false on timeout == //
    // After a step command, VsBridge waits for VS to re-enter break mode.
    // If it doesn't happen within the timeout, we return false rather than hang.
    [Fact]
    public void WaitForBreakMode_ReturnsFalse_WhenStaysInRunMode()
    {
        var dte = CreateMockDte(dbgDebugMode.dbgRunMode);

        var result = VsConnection.WaitForBreakMode(dte, maxWaitMs: 300);

        Assert.False(result);
    }

    // == WaitForBreakMode returns true when already in break mode == //
    // If the debugger is already paused, WaitForBreakMode should return
    // true immediately (within the first polling cycle).
    [Fact]
    public void WaitForBreakMode_ReturnsTrue_WhenAlreadyBreakMode()
    {
        var dte = CreateMockDte(dbgDebugMode.dbgBreakMode);

        var result = VsConnection.WaitForBreakMode(dte, maxWaitMs: 500);

        Assert.True(result);
    }

    // == Helper: create a mock DTE2 with a specific debugger mode == //
    // Uses Mock<Debugger> (base type) since DTE2.Debugger returns Debugger.
    private static DTE2 CreateMockDte(dbgDebugMode mode)
    {
        var mockDebugger = new Mock<Debugger>();
        mockDebugger.Setup(d => d.CurrentMode).Returns(mode);

        var mockDte = new Mock<DTE2>();
        mockDte.Setup(d => d.Debugger).Returns(mockDebugger.Object);

        return mockDte.Object;
    }
}

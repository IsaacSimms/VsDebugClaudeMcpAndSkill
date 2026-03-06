# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Project Is

VsBridge is an MCP (Model Context Protocol) server that lets Claude Code control Visual Studio's debugger (2022 and 2026) via COM automation (EnvDTE). It's a single .NET 8 console app that acts as both the MCP server and the COM bridge — no Node.js layer needed.

```
Claude Code --(MCP stdio)--> VsBridge.exe --(COM/EnvDTE)--> Visual Studio
```

## Build

```bash
dotnet build VsBridge/VsBridge.csproj -c Release
```

The MCP server is registered in `.mcp.json` using `dotnet run`, so building separately is only needed for verification.

## Architecture

**Program.cs** — MCP host entry point. Uses `Host.CreateEmptyApplicationBuilder` (not `CreateDefaultBuilder`) to avoid stdout pollution that would corrupt the MCP stdio protocol.

**ComHelper.cs** — P/Invoke wrappers for `CLSIDFromProgID` (ole32.dll) and `GetActiveObject` (oleaut32.dll), replacing `Marshal.GetActiveObject` which was removed in .NET Core. Enumerates the Windows Running Object Table (ROT) to find all VS instances, preferring the newest version.

**StaDispatcher.cs** — Dedicated STA thread with a blocking queue. All COM calls must run on an STA thread, but the MCP SDK dispatches requests on thread pool threads (MTA). Every EnvDTE call goes through `StaDispatcher.Invoke()`.

**VsConnection.cs** — Singleton managing the DTE2 COM connection. Lazily connects on first tool call, auto-reconnects if VS restarts (catches `COMException` from stale references). Provides `Execute()` to run lambdas against the DTE on the STA thread, plus helpers like `EnsureBreakMode()` and `WaitForBreakMode()`.

**DebuggerTools.cs** — All 11 MCP tools as static methods with `[McpServerTool]` attributes. `VsConnection` is injected via DI. Every tool returns a string (never throws to the MCP layer) so Claude always gets a usable response.

## Key Constraints

- **STA threading is non-negotiable.** All EnvDTE COM calls must go through `StaDispatcher`. Direct COM calls from async/thread-pool code will fail or deadlock.
- **Never write to stdout** except through the MCP SDK. Any `Console.WriteLine` corrupts the JSON-RPC stdio transport.
- **VS must be running** at the same elevation level as Claude Code for COM access to work.
- Tool classes must be `public` (MCP SDK requirement for reflection-based discovery). `VsConnection` and `StaDispatcher` are also public because they're DI-injected into public tool methods.

## Adding a New Tool

Add a static method to `DebuggerTools.cs`:
```csharp
[McpServerTool(Name = "vs_tool_name"), Description("Description for Claude")]
public static string ToolName(VsConnection vs, [Description("param help")] string param)
{
    try
    {
        return vs.Execute(dte => { /* COM work here */ return "result"; });
    }
    catch (Exception ex)
    {
        return $"Error: {ex.Message}";
    }
}
```
The MCP SDK auto-discovers it via `WithToolsFromAssembly()`. No registration code needed.

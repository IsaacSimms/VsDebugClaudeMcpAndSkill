# VsBridge

An MCP server that gives Claude Code full control over the Visual Studio debugger. Set breakpoints, launch, step, inspect locals, evaluate expressions — all through natural language in your terminal.

```
Claude Code ──(MCP stdio)──> VsBridge.exe ──(COM / EnvDTE)──> Visual Studio
```

No VS extension needed. No Node.js. A single .NET 8 console app bridges Claude Code to any running instance of Visual Studio 2022 or 2026 via COM automation.

## What It Does

Ask Claude to debug your code, and it will:

1. Connect to your running Visual Studio instance
2. Set breakpoints where you tell it (or where it thinks the bug is)
3. Launch the debugger (F5)
4. Inspect locals, evaluate expressions, read the call stack
5. Step through code line by line
6. Report back what it found

All 11 tools are exposed as MCP tools that Claude Code can call directly:

| Tool | What It Does | Needs Break Mode |
|---|---|:---:|
| `vs_status`            | Debugger state, solution name, active breakpoints | |
| `vs_launch`            | Start debugging (F5) or resume from break | |
| `vs_stop`              | Stop debugging (Shift+F5) | |
| `vs_set_breakpoint`    | Set breakpoint at file:line, optional condition | |
| `vs_remove_breakpoint` | Remove breakpoint at file:line | |
| `vs_get_locals`        | Local variables and values at current frame | ✓ |
| `vs_evaluate`          | Evaluate any C# expression in context | ✓ |
| `vs_step_over`         | Step over (F10) | ✓ |
| `vs_step_into`         | Step into (F11) | ✓ |
| `vs_step_out`          | Step out (Shift+F11) | ✓ |
| `vs_get_callstack`     | Full call stack with module info | ✓ |

## Prerequisites

- **Windows** — COM interop is Windows-only
- **Visual Studio 2022 or 2026** — must be running with a solution open
- **[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)** or later
- **[Claude Code](https://docs.anthropic.com/en/docs/claude-code)** CLI

## Installation

### Option A — Publish and register globally (recommended)

This makes VsBridge available to Claude Code in **every** project, with instant startup.

**1. Clone**

````````powershell
$installDir = "$env:USERPROFILE\.claude\VsBridge"
git clone https://github.com/IsaacSimms/VsDebugClaudeMcpAndSkill.git
cd VsDebugClaudeMcpAndSkill
dotnet publish VsBridge/VsBridge.csproj -c Release -o $installDir
```

**2. Register with Claude Code**

```powershell
claude mcp add vs-debugger -- "$installDir\VsBridge.exe"
```

Or add it manually to `~/.claude/settings.json`:

```json
{
  "mcpServers": {
    "vs-debugger": {
      "command": "C:\\Users\\YourName\\.claude\\VsBridge\\VsBridge.exe"
    }
  }
}
```

**3. Install the skill (optional, recommended)**

The skill file teaches Claude *when and how* to use the debugger tools:

```bash
cp SKILL.md ~/.claude/skills/vs-debugger.md
```

**Updating after code changes:** just run `.\install.ps1` again. The script stops any running VsBridge process automatically before overwriting the exe.

### Option B — Run from source (per-project)

If you prefer `dotnet run` (recompiles on each launch, adds a few seconds of startup):

**1. Clone**

```bash
git clone https://github.com/IsaacSimms/VsDebugClaudeMcpAndSkill.git
```

**2. Add `.mcp.json` to any project**

Create a `.mcp.json` in the root of the project you want to debug:

```json
{
  "mcpServers": {
    "vs-debugger": {
      "command": "dotnet",
      "args": ["run", "--project", "C:\\path\\to\\VsDebugClaudeMcpAndSkill\\VsBridge\\VsBridge.csproj", "-c", "Release"]
    }
  }
}
```

Replace `C:\\path\\to\\` with the actual path where you cloned the repo.

## Verify It Works

1. Open any solution in Visual Studio
2. Open Claude Code in a terminal
3. Ask:

```
Check the status of my Visual Studio debugger
```

You should see output like:

```
Debugger Mode: Design (not debugging)
Solution: C:\Projects\MyApp\MyApp.sln
```

## Usage

### Ask Claude naturally

```
> There's a NullReferenceException in OrderService.ProcessOrder around line 47.
  Set a breakpoint there, launch the debugger, and tell me what's null.
```

```
> Step through the CalculateTotal method and show me how the discount
  variable changes on each iteration.
```

```
> Run the app with a breakpoint at Startup.cs:23 and evaluate
  Configuration["ConnectionStrings:Default"] when it hits.
```

### Or use the explicit workflow

1. `vs_status` — check VS is connected and see current state
2. `vs_set_breakpoint` — place breakpoints at lines of interest
3. `vs_launch` — start debugging (F5)
4. `vs_get_locals` — inspect local variables when paused
5. `vs_evaluate` — evaluate expressions like `myList.Count`, `customer?.Name`
6. `vs_get_callstack` — see how execution reached the current point
7. `vs_step_over` / `vs_step_into` / `vs_step_out` — navigate code
8. `vs_stop` — end the debug session

## Important: Elevation Must Match

Visual Studio and the terminal running Claude Code **must run at the same elevation level** — either both as admin, or both as a normal user. If they don't match, COM access will fail with a connection error.

## Architecture

```
┌─────────────┐     JSON-RPC      ┌──────────────┐    COM/STA     ┌──────────────────┐
│ Claude Code  │ ◄──(stdin/out)──► │  VsBridge    │ ◄────────────► │ Visual Studio    │
│  (MCP client)│                   │  (MCP server)│                │ (DTE2 via EnvDTE)│
└─────────────┘                   └──────────────┘                └──────────────────┘
```

| File | Purpose |
|---|---|
| `Program.cs`       | MCP host — stdio transport, DI wiring |
| `DebuggerTools.cs` | 11 MCP tools as `[McpServerTool]` static methods |
| `VsConnection.cs`  | DTE2 lifecycle — lazy connect, auto-reconnect on VS restart |
| `StaDispatcher.cs` | Dedicated STA thread — marshals all COM calls from MTA pool |
| `ComHelper.cs`     | P/Invoke wrappers, Running Object Table enumeration |

Key design decisions:

- **STA threading** — EnvDTE COM objects require STA. The MCP SDK dispatches on thread pool (MTA) threads. `StaDispatcher` bridges the two with a dedicated STA thread and a blocking queue.
- **`Host.CreateEmptyApplicationBuilder`** — avoids the default host's console logging, which would corrupt the MCP stdio JSON-RPC transport.
- **Tools never throw** — every tool catches exceptions and returns an error string, so Claude always gets a usable response.
- **Auto-reconnect** — if Visual Studio restarts mid-session, VsBridge detects the stale COM reference and reconnects on the next tool call.

## Troubleshooting

| Problem | Solution |
|---|---|
| "Visual Studio is not running" | Open VS with a solution loaded before calling any tools |
| Connection error / COM failure | Ensure VS and your terminal share the same elevation (both admin or both normal) |
| Tools hang or time out         | A COM call may be blocked — restart VS and try again |
| "Debugger is not paused"       | Inspection tools require break mode. Set a breakpoint with `vs_set_breakpoint`, then `vs_launch` |
| Wrong VS instance connects     | VsBridge picks the newest version. Close other VS instances to target a specific one |
| `dotnet run` startup is slow   | Use Option A (publish to exe) for instant startup |
| MCP server not found by Claude | Verify `.mcp.json` path is absolute, or use `claude mcp add` for global registration |

## Running Tests

```bash
dotnet test
```

All 33 tests use Moq to mock EnvDTE COM interfaces — no running Visual Studio instance required.

## Adding a New Tool

Add a static method to `DebuggerTools.cs`:

```csharp
[McpServerTool(Name = "vs_tool_name"), Description("Description for Claude")]
public static string ToolName(VsConnection vs, [Description("param help")] string param)
{
    try
    {
        return vs.Execute(dte =>
        {
            // COM work here
            return "result";
        });
    }
    catch (Exception ex)
    {
        return $"Error: {ex.Message}";
    }
}
```

The MCP SDK auto-discovers it via `WithToolsFromAssembly()`. No registration needed.

## License

MIT

The script handles everything — publishing the exe, installing the skill, and registering the MCP server in `~/.claude/settings.json`. It is safe to run again after any update to the project.

After running, your `.claude` folder will contain:

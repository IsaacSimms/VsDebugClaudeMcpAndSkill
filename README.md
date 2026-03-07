# VsBridge

VsBridge is an MCP server that lets [Claude Code](https://docs.anthropic.com/en/docs/claude-code) control the Visual Studio debugger. Once installed, Claude can set breakpoints, launch the debugger, step through code, inspect variables, and evaluate expressions — all from the terminal, against any project open in Visual Studio.

```
Claude Code ──(MCP / stdio)──> VsBridge.exe ──(COM / EnvDTE)──> Visual Studio
```

No VS extension. No Node.js. One .NET 8 exe talks directly to Visual Studio via COM automation.

## Requirements

- **Windows** — COM interop is Windows-only
- **Visual Studio 2022 or 2026** — must be running with a solution open
- **[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)** or later
- **[Claude Code](https://docs.anthropic.com/en/docs/claude-code)** CLI installed and authenticated

## Install

Clone the repo and run the install script. That's it.

```powershell
git clone https://github.com/IsaacSimms/VsDebugClaudeMcpAndSkill.git
cd VsDebugClaudeMcpAndSkill
.\install.ps1
```

The script output will look like this:

```
=== VsBridge Install / Update ===
  Publishing exe to C:\Users\YourName\.claude\VsBridge ...
  Exe ready:     C:\Users\YourName\.claude\VsBridge\VsBridge.exe
  Skill updated: C:\Users\YourName\.claude\skills\vs-debugger.md
  MCP entry added to settings.json

  Done. Restart Claude Code to pick up changes.
```

After it finishes, your `.claude` folder will contain:

```
~/.claude/
├── VsBridge/
│   └── VsBridge.exe          ← the MCP server Claude Code will launch
├── skills/
│   └── vs-debugger.md        ← teaches Claude when and how to use the tools
└── settings.json             ← vs-debugger entry added here automatically
```

**Then restart Claude Code.** The MCP server starts automatically when Claude Code launches.

> **Note:** Visual Studio and the terminal running Claude Code must be at the **same elevation level** — both normal user, or both admin. COM access across elevation boundaries will fail.

## Verify It Works

Open a solution in Visual Studio, then in Claude Code ask:

```
check the status of my visual studio debugger
```

Expected response:

```
Debugger Mode: Design (not debugging)
Solution: C:\Projects\MyApp\MyApp.sln
```

If you see that, VsBridge is connected and ready.

## Usage

Just ask Claude naturally:

```
There's a bug in OrderService around line 47. Set a breakpoint there,
launch the debugger, and tell me what's null when it hits.
```

```
Step through the CalculateTotal method and show me how the
running total changes on each loop iteration.
```

```
Set a breakpoint at Startup.cs:23, launch, and evaluate
Configuration["ConnectionStrings:Default"] when it pauses.
```

Claude will call the appropriate tools in sequence automatically.

### Tools

| Tool | What It Does | Requires Break Mode |
|---|---|:---:|
| `vs_status` | Debugger state, solution name, active breakpoints | |
| `vs_launch` | Start debugging (F5) or resume from break | |
| `vs_stop` | Stop debugging (Shift+F5) | |
| `vs_set_breakpoint` | Set breakpoint at file:line, optional condition | |
| `vs_remove_breakpoint` | Remove breakpoint at file:line | |
| `vs_get_locals` | Local variables and values at current frame | ✓ |
| `vs_evaluate` | Evaluate any C# expression | ✓ |
| `vs_step_over` | Step over (F10) | ✓ |
| `vs_step_into` | Step into (F11) | ✓ |
| `vs_step_out` | Step out (Shift+F11) | ✓ |
| `vs_get_callstack` | Full call stack with module info | ✓ |

"Break mode" means the debugger is paused — at a breakpoint, or after a step command.

## Updating

After pulling new changes, just run the install script again:

```powershell
.\install.ps1
```

It stops any running VsBridge process before overwriting the exe, so there's no need to close Claude Code first. Restart Claude Code once the script finishes.

## Per-Project Use (Alternative)

If you want VsBridge only for a specific project instead of globally, add a `.mcp.json` to that project's root:

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

This uses `dotnet run` and recompiles on each launch (a few seconds of startup delay). The global install from `install.ps1` is faster and available in every project.

## Troubleshooting

| Problem | Fix |
|---|---|
| "Visual Studio is not running" | Open VS with a solution loaded before calling any tools |
| COM error / can't connect | Make sure VS and your terminal are at the same elevation (both admin or both not) |
| Tools hang or time out | A COM call may be stuck — restart VS and try again |
| "Debugger is not paused" | Use `vs_set_breakpoint` + `vs_launch` first — inspection tools only work when paused |
| Wrong VS instance connects | VsBridge picks the newest VS version — close other instances to target a specific one |
| MCP server not found after install | Confirm `~/.claude/settings.json` has the `vs-debugger` entry, then restart Claude Code |

## Running Tests

```powershell
dotnet test
```

All 33 tests mock the EnvDTE COM interfaces with Moq — no running Visual Studio required.

## Adding a New Tool

Add a static method to `DebuggerTools.cs`:

```csharp
[McpServerTool(Name = "vs_my_tool"), Description("What this tool does")]
public static string MyTool(VsConnection vs, [Description("param description")] string param)
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

`WithToolsFromAssembly()` in `Program.cs` discovers it automatically — no registration needed. Then run `.\install.ps1` to deploy the updated exe.

## License

MIT

After running, your `.claude` folder will contain:

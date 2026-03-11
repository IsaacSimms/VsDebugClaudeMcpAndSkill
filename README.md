# VsBridge

VsBridge is an MCP server that lets [Claude Code](https://docs.anthropic.com/en/docs/claude-code) control the Visual Studio debugger. Once installed, Claude can set breakpoints, launch the debugger, step through code, inspect variables, and evaluate expressions — all from the terminal, against any project open in Visual Studio.

```
Claude Code ──(MCP / stdio)──> VsBridge.exe ──(COM / EnvDTE)──> Visual Studio
```

No VS extension. No Node.js. One .NET 8 exe talks directly to Visual Studio via COM automation. Works with any language Visual Studio can debug — C#, C++, Python, TypeScript, and more.

---

## Requirements

- **Windows** — COM interop is Windows-only
- **Visual Studio 2022 or 2026** — must be running with a solution/project open
- **[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)** or later — needed to build VsBridge
- **[Claude Code](https://docs.anthropic.com/en/docs/claude-code)** CLI installed and authenticated

---

## Install

### Option A: Scripted (recommended)

Clone the repo and run the install script:

```powershell
git clone https://github.com/IsaacSimms/VsDebugClaudeMcpAndSkill.git
cd VsDebugClaudeMcpAndSkill
.\install.ps1
```

The script will:
1. Build and publish `VsBridge.exe` to `~/.claude/VsBridge/`
2. Copy the skill file to `~/.claude/skills/vs-debugger.md`
3. Register the MCP server in `~/.claude/.mcp.json`

Output looks like this:

```
=== VsBridge Install / Update ===
  Publishing exe to C:\Users\YourName\.claude\VsBridge ...
  Exe ready:     C:\Users\YourName\.claude\VsBridge\VsBridge.exe
  Skill updated: C:\Users\YourName\.claude\skills\vs-debugger.md
  MCP entry added to .mcp.json

  Done. Restart Claude Code to pick up changes.
```

### Option B: Manual

If you prefer to set things up yourself, or the script doesn't work in your environment:

**1. Build the exe**

```powershell
git clone https://github.com/IsaacSimms/VsDebugClaudeMcpAndSkill.git
cd VsDebugClaudeMcpAndSkill
dotnet publish VsBridge/VsBridge.csproj -c Release -o "$env:USERPROFILE\.claude\VsBridge"
```

**2. Register the MCP server**

Add the `vs-debugger` entry to your global Claude Code MCP config at `~/.claude/.mcp.json`. Create the file if it doesn't exist:

```json
{
  "mcpServers": {
    "vs-debugger": {
      "command": "C:\\Users\\YourName\\.claude\\VsBridge\\VsBridge.exe",
      "args": []
    }
  }
}
```

Replace `YourName` with your Windows username. If the file already has other MCP servers, add `vs-debugger` alongside them inside `mcpServers`.

**3. Install the skill file (optional but recommended)**

The skill file teaches Claude when and how to use the debugger tools. Copy it into your global skills folder:

```powershell
mkdir "$env:USERPROFILE\.claude\skills" -Force
copy SKILL.md "$env:USERPROFILE\.claude\skills\vs-debugger.md"
```

### After install

Your `~/.claude/` folder should now contain:

```
~/.claude/
├── .mcp.json               ← vs-debugger MCP server registered here
├── VsBridge/
│   └── VsBridge.exe         ← the MCP server binary
└── skills/
    └── vs-debugger.md       ← teaches Claude the debugging workflow
```

**Restart Claude Code** for the changes to take effect. The MCP server starts automatically when Claude Code launches.

> **Elevation matters:** Visual Studio and the terminal running Claude Code must be at the **same elevation level** — both as a normal user, or both as admin. COM access across elevation boundaries will fail silently.

---

## Verify It Works

Open a solution in Visual Studio, then in Claude Code run:

```
/mcp
```

You should see `vs-debugger` listed as a connected server. You can also ask Claude directly:

```
check the status of my visual studio debugger
```

Expected response:

```
Debugger Mode: Design (not debugging)
Solution: C:\Projects\MyApp\MyApp.sln
```

If you see that, VsBridge is connected and ready.

---

## Usage

Just ask Claude naturally:

```
There's a bug around line 47 in OrderService. Set a breakpoint there,
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
| `vs_evaluate` | Evaluate an expression in the current context | ✓ |
| `vs_step_over` | Step over (F10) | ✓ |
| `vs_step_into` | Step into (F11) | ✓ |
| `vs_step_out` | Step out (Shift+F11) | ✓ |
| `vs_get_callstack` | Full call stack with module info | ✓ |

"Break mode" means the debugger is paused — at a breakpoint, or after a step command.

---

## Updating

Pull the latest changes and run the install script again:

```powershell
git pull
.\install.ps1
```

The script stops any running VsBridge process before overwriting the exe, so there's no need to close Claude Code first. Restart Claude Code once the script finishes to load the updated server.

---

## Per-Project Use (Alternative)

The install steps above register VsBridge **globally** — it's available in every Claude Code session. If you'd rather scope it to a single project, add a `.mcp.json` to that project's root instead:

```json
{
  "mcpServers": {
    "vs-debugger": {
      "command": "C:\\Users\\YourName\\.claude\\VsBridge\\VsBridge.exe",
      "args": []
    }
  }
}
```

Or, if you haven't published the exe and want to build on the fly (slower startup):

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

---

## Troubleshooting

| Problem | Fix |
|---|---|
| "Visual Studio is not running" | Open VS with a solution loaded before calling any tools |
| COM error / can't connect | Make sure VS and your terminal are at the same elevation (both admin or both not) |
| Tools hang or time out | A COM call may be stuck — restart VS and try again |
| "Debugger is not paused" | Use `vs_set_breakpoint` + `vs_launch` first — inspection tools only work in break mode |
| Wrong VS instance connects | VsBridge picks the newest VS version; close other instances to target a specific one |
| MCP server not listed in `/mcp` | Check that `~/.claude/.mcp.json` has the `vs-debugger` entry, then restart Claude Code |
| Server listed but not connecting | Run `VsBridge.exe` manually in a terminal to check for startup errors |

---

## Running Tests

```powershell
dotnet test
```

All 33 tests mock the EnvDTE COM interfaces with Moq — no running Visual Studio required.

---

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

`WithToolsFromAssembly()` in `Program.cs` discovers it automatically — no registration needed. Run `.\install.ps1` afterward to deploy the updated exe.

---

## License

MIT

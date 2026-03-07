When debugging C# code, use the vs-debugger MCP tools to control Visual Studio's debugger directly.

## Available Tools

- `vs_status` - Check VS state (design/running/break mode), solution name, breakpoints
- `vs_set_breakpoint` - Set a breakpoint at file:line, with optional condition
- `vs_remove_breakpoint` - Remove a breakpoint at file:line
- `vs_launch` - Start debugging (F5) or resume from break
- `vs_stop` - Stop debugging (Shift+F5)
- `vs_get_locals` - Read local variables (break mode only)
- `vs_evaluate` - Evaluate any C# expression (break mode only)
- `vs_step_over` - Step over current line (F10)
- `vs_step_into` - Step into method call (F11)
- `vs_step_out` - Step out of current method (Shift+F11)
- `vs_get_callstack` - View the call stack (break mode only)

## Debugging Workflow

1. Call `vs_status` first to confirm VS is running and see the current state
2. Set breakpoints at relevant locations with `vs_set_breakpoint`
3. Start debugging with `vs_launch`
4. When paused at a breakpoint, inspect state with `vs_get_locals` and `vs_evaluate`
5. Use `vs_get_callstack` to understand how execution reached the current point
6. Step through code with `vs_step_over`, `vs_step_into`, or `vs_step_out`
7. Call `vs_stop` when done

## Tips

- Always call `vs_status` before other commands to know the current state
- Set breakpoints BEFORE the line you suspect is buggy
- Use `vs_evaluate` to test fix hypotheses without modifying code (e.g. `myList.Count`, `customer?.Name`)
- File paths can be full paths or just filenames if unique in the solution
- Most inspection tools require break mode — the debugger must be paused
- VS must be running as the same user and elevation level as Claude Code

# Repository Guidelines

## Project Structure & Module Organization
- `GpdControl.cs`: core command-line tool for reading, applying, and resetting controller mappings.
- `GpdGui.cs`: Windows GUI entry point (`GpdGui.Program`) that reuses logic from `GpdControl.cs`.
- `default_mappings.txt`: baseline mapping profile used with `apply`.
- `profiles/`: local profile storage used by `profile load` and `profile del`.
- `reference/`: upstream/reference materials; treat as read-only context, not active project code.
- `readme.md`: canonical usage and build notes.

## Build, Test, and Development Commands
Use Developer PowerShell on Windows.

```powershell
# Build CLI
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /target:exe /out:GpdControl.exe GpdControl.cs

# Build GUI
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /target:winexe /out:GpdGui.exe /main:GpdGui.Program GpdGui.cs GpdControl.cs
```

Common local checks:

```powershell
.\GpdControl.exe list
.\GpdControl.exe apply .\default_mappings.txt
.\GpdControl.exe reset
```

## Coding Style & Naming Conventions
- Language: C# (.NET Framework compiler via `csc.exe`).
- Indentation: 4 spaces; avoid tabs.
- Naming: `PascalCase` for types/methods/properties, `camelCase` for locals/parameters, `UPPER_CASE` only for true constants.
- Keep CLI verbs and options consistent with existing commands (`list`, `reset`, `profile`, `apply`, `set`).
- Prefer small, focused methods; share mapping logic between CLI and GUI rather than duplicating behavior.

## Testing Guidelines
There is no automated test suite yet. Validate changes with manual smoke tests:
1. Build both executables.
2. Run `list` and `reset` successfully.
3. Apply `default_mappings.txt` and confirm expected mapping output.
4. If GUI changes were made, launch `GpdGui.exe` and verify controls reflect CLI behavior.

## Commit & Pull Request Guidelines
- Current history is minimal (`Initial commit`), so use clear imperative messages, e.g. `Add profile validation for missing keys`.
- Keep commits scoped to one logical change.
- PRs should include: purpose and user-visible impact; validation steps with exact commands; screenshots for GUI changes; and a linked issue/task when applicable.

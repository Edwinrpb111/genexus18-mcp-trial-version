# AGENTS.md

Project-level instructions for AI assistants working on Genexus18MCP.

## Permissions granted to the assistant

- **Kill the Gateway when its lock blocks builds.** When `dotnet build` fails with `MSB3027/MSB3021` because `GxMcp.Gateway.exe` (any PID) is holding the output file, the assistant may stop it without asking:
  - `Stop-Process -Name GxMcp.Gateway -Force` (PowerShell), or `taskkill /IM GxMcp.Gateway.exe /F` (cmd).
  - Rationale: this is the user's own dev process; he prefers we just unblock the build rather than wait. Restart by re-running the MCP client / `dotnet run` afterwards if needed.

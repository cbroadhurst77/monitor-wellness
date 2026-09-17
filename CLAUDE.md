# MonitorWellness

## Obsidian vault (via `om` MCP)

This repo's work should be tracked in Chris's Obsidian vault at `C:\Users\chris\vaults\mind`. The `om` MCP server (registered in `.mcp.json`) exposes it here: `search`, `expand`, `recall`, `remember`, `record_work`, `reason`, `health`.

**Rules — not optional:**
- Do NOT implement a design, approach, or fix without first calling `recall` (this repo's scope) when the task is non-trivial or touches a prior decision. If the vault has already recorded that approach as rejected, broken, or superseded, stop and tell the user instead of proceeding.
- After finishing work worth remembering — a decision, a shipped fix, an incident, a lesson still true outside this repo — call `remember` or `record_work` so it reaches the vault.
- Never edit vault files directly from here. Go through the `om` MCP tools only.

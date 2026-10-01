# Unity MCP server integration for zoo code / VS Code

The Unity CLI ships with a built-in MCP stdio server (`unity mcp`).
It exposes the connected Unity Editor's commands as MCP tools so an AI client can drive Unity live.

## What the MCP server does

- `unity mcp` starts an MCP server over stdio.
- The server automatically discovers a running Unity Editor for the project.
- It lists the Editor's commands as MCP tools (`create_gameobject`, `save_scene`, `screenshot`, `eval`, etc.).
- The AI client can call these tools like any other MCP tool.

## Prerequisites

1. **Unity CLI** installed and on PATH (`unity --version`).
   - Windows install (PowerShell):
     ```powershell
     $env:UNITY_CLI_CHANNEL='beta'; irm https://public-cdn.cloud.unity3d.com/hub/prod/cli/install.ps1 | iex
     ```
   - Restart the shell after install and run `unity --version`.
2. **`com.unity.pipeline`** package installed in the project.
   - Install: `unity pipeline install --project-path "c:/Users/goddammit/Desktop/game/AAAA_game"`
3. **Unity Editor open** for the project.
4. `unity status` reports the Editor as `ready`.

> ️ On this machine the `unity` executable was not found in PATH. Install the CLI before the MCP server will work.

## Register as an MCP server in zoo code / VS Code

### Option A: VS Code user settings (`settings.json`)

Open VS Code settings (JSON) and add:

```json
{
  "zooCode.mcpServers": {
    "unity": {
      "command": "unity",
      "args": [
        "mcp",
        "--project-path",
        "c:/Users/goddammit/Desktop/game/AAAA_game"
      ]
    }
  }
}
```

If your extension uses the standard MCP server configuration, it may read from `mcpServers` at the VS Code level or from a workspace file (see below).

### Option B: workspace-level `.vscode/mcp.json` or `.vscode/settings.json`

Some MCP clients read `.vscode/mcp.json` or a similar file. Example:

```json
{
  "mcpServers": {
    "unity": {
      "command": "unity",
      "args": [
        "mcp",
        "--project-path",
        "c:/Users/goddammit/Desktop/game/AAAA_game"
      ]
    }
  }
}
```

### Option C: project-level config in the repository

If your AI client supports per-project MCP configuration, commit the server definition into the repo, for example `.roo/mcp.json` or `.zoo/mcp.json`:

```json
{
  "mcpServers": {
    "unity": {
      "command": "unity",
      "args": [
        "mcp",
        "--project-path",
        "c:/Users/goddammit/Desktop/game/AAAA_game"
      ]
    }
  }
}
```

Check your client's documentation for the exact key name; common ones are `mcpServers`, `servers`, or `mcp`.

## Verifying the connection

After saving the configuration and reloading the AI client:

1. Make sure the Unity Editor is open and `unity status` returns `ready`.
2. The client should list available MCP tools.
3. You should see tools like `create_gameobject`, `save_scene`, `screenshot`, etc.

## Notes

- `unity mcp` uses stdio. The AI client must spawn it as a subprocess and communicate over stdin/stdout.
- The server does not need the Editor to be running at startup; if the Editor is opened later, the server reports the new tools via `tools/list_changed`.
- If multiple projects are open, always pass `--project-path` to avoid the `AMBIGUOUS_EDITOR` error.

## References

- [`skills/unity-cli/SKILL.md`](skills/unity-cli/SKILL.md) — full Unity CLI reference.
- [`skills/unity-cli/references/integration-advanced.md`](skills/unity-cli/references/integration-advanced.md) — MCP and live Editor details.

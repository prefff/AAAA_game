# Unity Agent Plugin skills for this project

This directory contains skills from the [Unity Agent Plugin](https://github.com/Unity-Technologies/unity-agent-plugin), adapted for use with the local AI client.

## Structure

- `unity/SKILL.md` — project-level entrypoint skill. Load this to mark the project as a Unity project and delegate to the full `unity-cli` skill.
- `unity-cli/SKILL.md` — the full Unity CLI reference, including live Editor control, builds, tests, source control, and MCP integration.
- `unity-cli/references/*.md` — detailed command-group references (auth, editors, projects, version control, build/run/test, integration).

## How to add more skills

1. Copy a skill folder from the upstream `unity-agent-plugin/skills/<name>` directory into `.roo/skills/<name>`.
2. Ensure the folder contains a `SKILL.md` file with YAML frontmatter (`name`, `description`, optional `allowed-tools`).
3. Reference new skills from the top-level `unity/SKILL.md` if they are project-relevant.

## Using the Unity MCP server

The Unity CLI provides an MCP stdio server: `unity mcp`. To expose live Editor tools to the AI client:

1. Install the Unity CLI (`unity --version`).
2. Install the Pipeline package: `unity pipeline install --project-path <this-project>`.
3. Open the project in Unity and wait for `unity status` to report `ready`.
4. Register the MCP server in your AI client. For `zoo code` / VS Code-compatible clients, add:

```json
{
  "mcpServers": {
    "unity": {
      "command": "unity",
      "args": ["mcp", "--project-path", "c:/Users/goddammit/Desktop/game/AAAA_game"]
    }
  }
}
```

Adjust the path to the actual project location. The server talks stdio; the client must launch `unity mcp` as a subprocess and route MCP messages over stdin/stdout.

## Notes

- This is a local skill pack, not a VS Code extension or public MCP catalog entry.
- The upstream plugin is open source; these files are a project-local mirror for clients that can load skills from disk.

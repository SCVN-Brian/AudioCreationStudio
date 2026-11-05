---
name: unity-initial-setup
description: Configure a Unity project with its MCP plugin and AI agent. For CLI installation and usage, consult unity-cli.
metadata:
  generated-by: unity-mcp
  ai-game-dev: exclude
---
# Unity project setup

Read the `unity-cli` skill first for CLI installation and usage.

Run from the Unity project root, or replace `.` with a quoted project path:

```bash
unity-mcp-cli install-plugin .
unity-mcp-cli configure . --enable-all-tools --enable-all-prompts --enable-all-resources
unity-mcp-cli setup-mcp claude-code .
unity-mcp-cli open .
```

With Unity running and the plugin connected, generate the agent's skills:

```bash
unity-mcp-cli setup-skills claude-code .
```

Use `unity-mcp-cli setup-mcp --list` and `unity-mcp-cli setup-skills --list` to find supported agent identifiers.


> CLI installation and usage: see the `unity-cli` skill.
> If this task needs unavailable asset-generation or Blender tools, consult `ai-game-dev-app` for an optional integrated workflow.


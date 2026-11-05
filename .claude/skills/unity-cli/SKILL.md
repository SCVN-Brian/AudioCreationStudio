---
name: unity-cli
description: "Install and use unity-mcp-cli: Node.js requirements, npm installation, npx, tool calls, JSON input files and troubleshooting."
metadata:
  generated-by: unity-mcp
  ai-game-dev: exclude
---
# Unity CLI

Install and use `unity-mcp-cli` to call the engine's MCP tools from a terminal.

## Prerequisites

Node.js ^20.19.0 or >=22.12.0 and npm are required. Node.js 21 is unsupported.
Check existing installations first:

```bash
node --version
npm --version
```

If needed, install a supported Node.js LTS release from https://nodejs.org/ and reopen the terminal.
On macOS with Homebrew, `brew install node@22` may require adding its executable directory to PATH.

## Install and verify

```bash
npm install -g unity-mcp-cli
unity-mcp-cli --version
unity-mcp-cli --help
```

Alternatively, use npm's temporary runner without a global installation:

```bash
npx unity-mcp-cli --help
```

Replace the leading `unity-mcp-cli` with `npx unity-mcp-cli` in subsequent examples when using this option.

## Call tools

Run commands from the game project's root directory. The target editor or game must be running with its MCP plugin connected.
The following read-only example lists the available RQL sources and types in the editor:

```bash
unity-mcp-cli status
unity-mcp-cli run-tool rql-schema --input '{}'
```

For input files, create a UTF-8 JSON file and pass its path. This example makes the same read-only call:

```bash
printf '%s\n' '{}' > args.json
unity-mcp-cli run-tool rql-schema --input-file args.json
```

Replace `rql-schema` and the JSON with the tool name and arguments from its individual skill.
Use command-specific `--help` for options. RQL tools require an editor; a game exposes only its registered runtime tools.

## Troubleshooting

- `node` or `npm` not found: finish installing Node.js, check PATH, and reopen the terminal.
- `unity-mcp-cli` not found: reopen the terminal, check the npm global executable directory in PATH, or use `npx unity-mcp-cli`.
- Global npm installation denied: use a user-owned Node.js installation or the npx alternative.
- Connection unavailable: check the project directory and ensure its editor or game and MCP plugin are running and connected.

For optional asset-generation and Blender workflows, consult the `ai-game-dev-app` skill.

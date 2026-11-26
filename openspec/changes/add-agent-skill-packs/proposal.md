# Proposal

## Why

This repository is a .NET 10 platform built on Duende IdentityServer 8 and Aspire 17, but its OpenCode sessions have no domain skills for that stack: every IdentityServer configuration, Aspire wiring, or .NET build question is answered from general model knowledge. Three vendor-maintained packs now cover exactly these areas — Duende Agent Skills, the .NET team's `dotnet/skills`, and official Aspire skills. All three are distributed primarily for Claude/Copilot/Cursor/Codex plugin marketplaces, which OpenCode does not support, so installing them requires selecting the portable components (skills, subagents, MCP) and deliberately dropping host-specific ones. OpenCode V2 accepts `lsp` configuration but does not run language servers, so Roslyn diagnostics are explicitly out of scope; C# diagnostics remain the job of `dotnet build` and `dotnet test`.

## What Changes

- Vendor all 26 **Duende Agent Skills** into `.opencode/skills/duende/`, pinned to commit `aa76086` of `DuendeSoftware/duende-skills`.
- Convert the two Duende specialists (`identity-server-specialist`, `oauth-oidc-specialist`) into project subagents under `.opencode/agents/`, since OpenCode has no Claude agent-plugin mechanism.
- Vendor the curated .NET plugin set — `dotnet`, `dotnet-aspnetcore`, `dotnet-diag`, and `dotnet-test` (38 skills) — into `.opencode/skills/dotnet/`, pinned to commit `a273ceb` of `dotnet/skills`; omit `dotnet-msbuild` and `dotnet-upgrade` as redundant with the core `msbuild`, `csharp-refactoring`, and `dotnet-breaking-changes` skills.
- Register the 8 **Aspire skills** through Aspire's official OpenCode V2 HTTP catalog, pinned to commit `e529b60` of `microsoft/aspire-skills`, in the project `skills` array.
- Do **not** configure a language server: OpenCode V2 preserves `lsp` configuration but does not run language servers, expose LSP tools, or produce diagnostics. The .NET skills and `csharp-expert` fall back to `dotnet build`/`dotnet test` for C# diagnostics.
- Add the free remote **Microsoft Learn MCP** server (`https://learn.microsoft.com/api/mcp`) alongside the existing NuGet and Duende servers, and enable `learn.microsoft.com` egress in the dotnet kit's runtime network policy so every kit-created sandbox can reach it without a per-session approval.
- Add a provenance document plus a sync script and a small `update-agent-skills` skill that refresh the vendored packs, re-pin the Aspire catalog URL, and support a drift check.
- **Explicitly not installed:** Claude/Copilot/Cursor/Codex plugin marketplaces, Copilot `.agent.md` custom agents, Aspire Copilot canvas extensions, the `dotnet-msbuild` binlog MCP tool, and Roslyn LSP diagnostics. OpenCode V2 accepts but does not run `lsp` configuration, so these are recorded as non-goals rather than silently dropped.
- **BREAKING**: none. Existing MCP registrations, OpenSpec skills, and application code are untouched.

## Capabilities

### New Capabilities

- `agent-skills`: Which skill packs and specialist subagents an OpenCode session in this repository exposes, how each pack is sourced and version-pinned, and how unsupported host-specific pack components are handled.

### Modified Capabilities

- `agent-mcp-servers`: Adds the Microsoft Learn remote MCP server and its egress requirement; generalizes the portability requirement from "both servers" to all configured servers.

## Impact

- `.opencode/opencode.json` — add the Aspire `skills` catalog URL and `mcp.servers.microsoft-learn`; keep `providers` and existing MCP entries intact.
- `.opencode/skills/duende/**`, `.opencode/skills/dotnet/**` — vendored `SKILL.md` files plus their `references/`, `docs/`, and `assets/` supporting files.
- `.opencode/agents/identity-server-specialist.md`, `.opencode/agents/oauth-oidc-specialist.md` — adapted from the Duende Claude agents.
- `.opencode/skills/README.md` — provenance, pins, inclusion rationale, and update procedure.
- `.opencode/skills/sync-agent-skills.sh`, `.opencode/skills/sources.json`, `.opencode/skills/update-agent-skills/SKILL.md` — update mechanism for vendored packs and the Aspire pin.
- `sbx/kits/dotnet/dotnet.yaml` — `learn.microsoft.com:443` runtime egress.
- No language-server configuration: OpenCode V2 preserves but does not execute `lsp`; C# diagnostics use `dotnet build`/`dotnet test`.
- No application code, packages, or runtime APIs change. Skill content is advisory context; it does not alter build or deployment behavior.

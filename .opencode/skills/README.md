# Agent skill packs

This directory pins the vendor-maintained skills that OpenCode sessions in this
repository expose. `.opencode/skills` is a native project skill source: each
`<id>/SKILL.md` directory is a skill, and supporting `docs/`, `references/`,
`assets/`, and script files stay beside it. The packs are project-local so they
survive sandbox recreation and are reviewed in Git.

`sources.json` is the machine-readable source of truth. This document records
the same provenance, the inclusion decisions, and the update procedure. Keep the
two in sync whenever a pack is refreshed.

## Sources

| Pack | Upstream repository | Pinned ref | Mode | Destination |
| --- | --- | --- | --- | --- |
| Duende | `DuendeSoftware/duende-skills` | `aa760867c3e3de167b41dff7e26b4002caf051b1` | vendored | `.opencode/skills/duende/` |
| .NET | `dotnet/skills` | `a273ceb823340c260b796ef49113b399430472a9` | vendored | `.opencode/skills/dotnet/` |
| Aspire | `microsoft/aspire-skills` | `e529b608336cd41c1b1262aa76d00d9e4ef1bff8` | catalog | `https://raw.githubusercontent.com/microsoft/aspire-skills/e529b608336cd41c1b1262aa76d00d9e4ef1bff8/opencode/v2/` |

The Aspire pack is the only non-vendored pack. OpenCode downloads it from the
pinned catalog URL and caches it under its data directory; the repository only
records the URL.

## Included and excluded components

### Duende

- **Included:** all 26 skills from the upstream `skills/` directory, with their
  `docs/`, scripts, and sibling-relative links intact. The pack root also keeps
  the upstream `LICENSE`.
- **Converted:** the two Duende specialists, `identity-server-specialist` and
  `oauth-oidc-specialist`, are project subagents in
  `.opencode/agents/identity-server-specialist.md` and
  `.opencode/agents/oauth-oidc-specialist.md`. Their Claude-only frontmatter was
  reduced to `description` and `mode: subagent`; the prompt bodies are the
  upstream bodies.
- **Excluded:** `.claude-plugin/`, `.codex-plugin/`, `.cursor-plugin/`, and
  `.agents/plugins/` marketplace metadata. OpenCode has no plugin-marketplace
  loader. The upstream `invocable: false` field is not interpreted by OpenCode
  and is kept as-is inside the vendored skill files.

### .NET

The `dotnet/skills` repository is organized as plugins. This project includes
four curated plugins and their skills:

| Plugin | Skills |
| --- | --- |
| `dotnet` | 5 |
| `dotnet-aspnetcore` | 2 |
| `dotnet-diag` | 7 |
| `dotnet-test` | 24 |
| **Total** | **38** |

The pack root keeps the upstream `LICENSE`. The selection matches this
repository's stack: Razor Pages, ASP.NET Core, Duende IdentityServer, Aspire,
xUnit v3/Microsoft.Testing.Platform, and central package management.

- **Excluded plugins:** `dotnet-msbuild` and `dotnet-upgrade` are omitted as
  redundant with the core `msbuild`, `csharp-refactoring`, and
  `dotnet-breaking-changes` skills. The unrelated `dotnet-ai`,
  `dotnet-advanced`, `dotnet-blazor`, `dotnet-data`, `dotnet-experimental`,
  `dotnet-maui`, `dotnet-nuget`, `dotnet-template-engine`,
  `dotnet-test-migration`, `dotnet-winforms`, and `dotnet11` plugins are also
  excluded; their skills do not apply to this repository, or they duplicate
  decisions already made here.
- **Excluded host-specific files:** the Copilot `.agent.md` agents in the
  selected plugins, plugin manifests, and `version.json`. The affected skills
  define inline fallbacks when their companion agent is not available.
- **Excluded LSP declaration:** the plugin's `lsp.json` Roslyn declaration is
  not translated. OpenCode V2 accepts and preserves `lsp` configuration but does
  not run language servers, expose LSP tools, or produce LSP diagnostics; C#
  diagnostics come from `dotnet build` and `dotnet test`, as the .NET skills
  already document.
- **Excluded MCP server:** the MSBuild binlog MCP server that ships with the
  excluded `dotnet-msbuild` plugin. The binlog skills are not installed.

### Aspire

- **Included:** the eight official OpenCode V2 catalog skills (`aspire`,
  `aspireify`, `aspire-init`, `aspire-orchestration`, `aspire-deployment`,
  `aspire-monitoring`, `aspire-terminals`, and
  `aspire-project-v2-migration`) with their references.
- **Excluded:** the Copilot app canvas extensions and `aspire agent init`
  plugin registration. OpenCode has no extension host, so the catalog is the
  supported installation route.

## Compatibility review

| Pack | Component | OpenCode support | Decision |
| --- | --- | --- | --- |
| Duende | 26 `SKILL.md` folders (+ `docs/`, `scripts/`, references) | Native skills, supporting files preserved | Vendor |
| Duende | `identity-server-specialist`, `oauth-oidc-specialist` Claude agents | Subagents after frontmatter conversion | Convert |
| Duende | `.claude-plugin`, `.codex-plugin`, `.cursor-plugin`, `.agents/plugins` | No marketplace/plugin loader | Omit |
| Duende | `invocable: false` frontmatter | Not interpreted; harmless unknown field | Keep as-is |
| .NET | Skills from 4 curated plugins (38) | Native skills | Vendor |
| .NET | `lsp.json` Roslyn declaration | V2 preserves but does not run LSP servers | Omit; use `dotnet build`/`dotnet test` diagnostics |
| .NET | `.agent.md` agents in the selected plugins | Copilot-only format | Omit; skills fall back inline |
| .NET | Plugin manifests, `version.json` | No plugin loader | Omit |
| .NET | MSBuild binlog MCP server | MCP supported but plugin excluded | Omit with `dotnet-msbuild` |
| Aspire | 8 skills, references, templates | Official OpenCode V2 HTTP catalog | Reference, pinned |
| Aspire | Copilot app canvas extensions | No extension host | Omit |
| Aspire | `aspire agent init` plugin registration | Host-specific | Do not use; catalog is the supported OpenCode route |
| MCP | Microsoft Learn remote endpoint | Native remote MCP | Add in `.opencode/opencode.json` |

## Licenses

All three upstream packs are MIT-licensed:

- Duende Agent Skills: MIT, Copyright (c) 2026 Duende Software.
- `dotnet/skills`: MIT, Copyright (c) .NET Foundation and Contributors.
- Aspire Skills: MIT, Copyright (c) Microsoft Corporation.

Vendored packs keep the upstream `LICENSE` at their pack root
(`duende/LICENSE`, `dotnet/LICENSE`). The Aspire content is downloaded from the
pinned upstream catalog, so its license travels with the upstream repository.
These licenses cover the vendored content, not the rest of this repository.

## Updating

The script needs `bash`, `curl`, `tar`, `python3`, and `mktemp`. It uses
`git diff --stat` only to summarize the result.

```bash
# Check whether installed content and the Aspire URL match sources.json.
# Exit 0 means clean; exit 1 lists every drift.
bash .opencode/skills/sync-agent-skills.sh --check

# Update every pack at the refs recorded in sources.json (the default).
bash .opencode/skills/sync-agent-skills.sh --update

# Update one pack only.
bash .opencode/skills/sync-agent-skills.sh --pack duende

# Move one pack to the current head of its upstream main branch.
bash .opencode/skills/sync-agent-skills.sh --pack dotnet --latest

# Pin an explicit 40-character commit and record it.
bash .opencode/skills/sync-agent-skills.sh --pack duende --ref duende=<sha>
```

An update downloads the `codeload.github.com` tarball at the selected ref into
a temporary directory, stages only the `include` paths listed in `sources.json`,
copies the upstream `LICENSE` to the pack root, and swaps the managed pack
directory into place. It then recomputes the pack's content hash in
`sources.json`. For the Aspire catalog it resolves the new URL and rewrites the
`skills` entry in `.opencode/opencode.json` without changing unrelated settings.

To add or remove an upstream component, edit that pack's `include` array in
`sources.json`, run the update for that pack, review the diff, and update this
table.

### Review checklist

- `git diff --stat` and the full skill diff show only the intended changes.
- Renamed, removed, or newly added skill IDs are reflected in the tables above.
- The counts are still 26 (Duende), 38 (.NET), and 8 (Aspire), or the diff
  explains why they changed.
- `find .opencode/skills -name SKILL.md -printf '%h\n' | xargs -n1 basename | sort | uniq -d`
  prints nothing.
- `bash .opencode/skills/sync-agent-skills.sh --check` exits 0.
- The refs in this README and `sources.json` are identical, and the Aspire
  catalog remains the only non-vendored pack.

Use the `update-agent-skills` skill when asking an agent to perform this
workflow; it encodes the same steps.

# Design

## Context

See `proposal.md` — Why. Constraints that shape the approach:

- OpenCode V2 (2.4.1 here) discovers skills from `.opencode/skills` (native), `.claude/skills`, and `.agents/skills`, plus local paths or HTTP catalogs in the `skills` config array. A skill is a directory containing `SKILL.md`; grouping directories are allowed up to six levels deep, and the skill ID is the containing directory name.
- OpenCode supports Markdown subagents under `.opencode/agents`, custom LSP servers via `lsp`, and local or remote MCP servers via `mcp.servers`. It does **not** support Claude/Copilot/Cursor/Codex plugin marketplaces, Copilot `.agent.md` agents, or Copilot canvas extensions.
- `.agents/skills` in this repository is owned by OpenSpec (`.openspec-target` = `opencode`). The new packs must not live there or they risk colliding with OpenSpec-managed content.
- The global `~/.config/opencode` directory is not persisted across `sbx env` recreation; project files are. Installation therefore has to be project-local.
- The OpenSpec kit already grants `*.githubusercontent.com:443`, so the Aspire catalog on `raw.githubusercontent.com` is reachable. `codeload.github.com` and `api.github.com` are also already granted, which covers the update script.
- `learn.microsoft.com:443` is not in the composed policy. In the current sandbox it returns an approval-required block, so a host approval is only needed for pre-rebuild verification; the kit entry is what enables future sandboxes.

## Goals / Non-Goals

**Goals:**

- All three vendor packs discoverable in a project session without per-session setup.
- A single, scripted update path with a drift check and a reviewable diff.
- Keep the repository's existing OpenSpec skills, MCP servers, and providers untouched.

**Non-Goals:**

- Reproducing Claude/Copilot plugin behavior (marketplaces, slash commands, agent handoffs, canvas extensions).
- Porting the `dotnet-test` and `dotnet-msbuild` Copilot agent pipelines; their skills already define inline fallbacks.
- Registering the MSBuild binlog MCP server. The affected skills fall back to binlog replay, and the extra server was not selected.
- Auto-updating on session start. Updates are explicit and committed.

## Pack compatibility review

| Pack | Component | OpenCode support | Decision |
| --- | --- | --- | --- |
| Duende | 26 `SKILL.md` folders (+ `docs/`, `scripts/`, references) | Native skills, supporting files preserved | Vendor |
| Duende | `identity-server-specialist`, `oauth-oidc-specialist` Claude agents | Subagents after frontmatter conversion | Convert |
| Duende | `.claude-plugin`, `.codex-plugin`, `.cursor-plugin`, `.agents/plugins` | No marketplace/plugin loader | Omit |
| Duende | `invocable: false` frontmatter | Not interpreted; harmless unknown field | Keep as-is |
| .NET | Skills from 6 curated plugins (65) | Native skills | Vendor |
| .NET | `lsp.json` Roslyn declaration | Custom `lsp` config | Translate |
| .NET | `.agent.md` agents (13 across test/msbuild/migration) | Copilot-only format | Omit; skills fall back inline |
| .NET | Plugin manifests, `version.json` | No plugin loader | Omit |
| .NET | MSBuild binlog MCP server | MCP supported but not selected | Omit; skill uses replay fallback |
| Aspire | 8 skills, references, templates | Official OpenCode V2 HTTP catalog | Reference, pinned |
| Aspire | Copilot app canvas extensions | No extension host | Omit |
| Aspire | `aspire agent init` plugin registration | Host-specific | Do not use; catalog is the supported OpenCode route |
| MCP | Microsoft Learn remote endpoint | Native remote MCP | Add |

## Decisions

### D1 — Project-local `.opencode/skills`, grouped by pack

```
.opencode/skills/
├── README.md
├── sources.json
├── sync-agent-skills.sh
├── update-agent-skills/SKILL.md
├── duende/<skill>/SKILL.md
├── dotnet/<skill>/SKILL.md
└── aspire/  (not vendored — catalog)
```

Rationale: native OpenCode location, highest discovery precedence, survives sandbox recreation, and is reviewable in Git. `.agents/skills` is rejected because OpenSpec owns it; `npx skills` is rejected because it writes to `.agents/skills`, needs npm registry egress, and cannot express the curated plugin list. Grouping keeps provenance clear and preserves Duende's sibling-to-sibling links (`../identityserver-spaces/SKILL.md`), which resolve inside `duende/`. IDs come from directory names; the selected sets have no duplicate IDs (verified during planning).

### D2 — Vendor Duende and .NET from immutable commits

Duplicate the selected folders from a `codeload.github.com` tarball at a full commit SHA and commit the result:

- Duende `aa760867c3e3de167b41dff7e26b4002caf051b1` — all 26 skills, 40 files, ~892 KB.
- .NET `a273ceb823340c260b796ef49113b399430472a9` — plugins `dotnet` (5), `dotnet-aspnetcore` (2), `dotnet-diag` (7), `dotnet-msbuild` (21), `dotnet-test` (24), `dotnet-upgrade` (6); 195 files.

Copy each upstream `LICENSE` into the pack root. Excluded .NET plugins: `dotnet-ai`, `dotnet-advanced`, `dotnet-blazor`, `dotnet-data`, `dotnet-experimental`, `dotnet-maui`, `dotnet-nuget`, `dotnet-template-engine`, `dotnet-test-migration`, `dotnet-winforms`, `dotnet11`. Rationale: this repository is Razor Pages + IdentityServer + Aspire on .NET 10, with central package management already in place and xUnit v3/Microsoft.Testing.Platform already selected.

### D3 — Aspire uses its official OpenCode V2 catalog, pinned by URL

`.opencode/opencode.json` gets:

```json
"skills": ["https://raw.githubusercontent.com/microsoft/aspire-skills/e529b608336cd41c1b1262aa76d00d9e4ef1bff8/opencode/v2/"]
```

Rationale: Aspire is the only vendor that ships and regenerates an OpenCode-native catalog with content hashes. Vendoring 8 skills would fork content the vendor already maintains for this exact client. Pinning the commit in the URL keeps it immutable; OpenCode caches downloaded skills under its data directory, so only a fresh cache needs the network. Re-pinning is a one-line change the sync script performs.

### D4 — Duende specialists become OpenCode subagents

Create `.opencode/agents/identity-server-specialist.md` and `.opencode/agents/oauth-oidc-specialist.md` with:

```yaml
---
description: <preserved upstream description>
mode: subagent
---
<body = upstream agent prompt>
```

Drop Claude-only fields (`name`, `tools`, `model`). No model is pinned; the subagent inherits the parent session's model. Rationale: OpenCode cannot load Claude agent files from a plugin, but its subagent format is a faithful mapping. `mode: subagent` keeps them out of the primary-agent picker and available to the `subagent` tool.

**Review of Duende's official OpenCode guidance** (`duende-skills` README → OpenCode): its global paths (`~/.config/opencode/skills`, `~/.config/opencode/agents`) and per-skill `SKILL.md` + bundled-directory copy are correct for V2, and `npx skills … -a opencode -g` installs to that same global skills directory. Two differences matter here: (1) those Claude agent files carry no `mode`, and a V2 custom agent defaults to `primary`, so the official `cp agents/*.md` step loads the specialists as top-level agents rather than subagents; (2) the recommended `npx` route is global, unpinned, needs npm registry egress, and this sandbox does not persist `~/.config/opencode`, while project-scope `npx` writes to `.agents/skills`, which OpenSpec owns in this repository. The guidance is therefore correct for a normal global setup but not for this repo; this design keeps its valid layout and adds `mode: subagent`, a pinned commit, and project-local installation. Its "directory names must match frontmatter `name`" note is conservative — V2 derives the ID from the directory name and treats `name` as a display label — and remains harmless because our directory names already match.

### D5 — Roslyn LSP replaces the built-in C# servers

Add a custom `roslyn` server using the command from the .NET plugin's `lsp.json`:

```json
"lsp": {
  "roslyn": {
    "command": ["dotnet", "dnx", "roslyn-language-server", "--yes", "--prerelease", "--", "--stdio", "--daemon-mode", "--autoLoadProjects"],
    "extensions": [".cs", ".razor", ".cshtml"]
  },
  "csharp": { "disabled": true },
  "razor": { "disabled": true }
}
```

Rationale: the built-in `csharp` server and the VS Code-dependent `razor` server would duplicate or compete with Roslyn. Disabling both keeps one server per file. The command uses `dotnet dnx`, so the first start may restore the tool; NuGet egress is already allowed. If the server rejects `.cshtml`/`.razor` because OpenCode cannot send the vendor's `aspnetcorerazor` language id, restrict `extensions` to `.cs` and record the limitation in the provenance document.

### D6 — Microsoft Learn is a remote MCP server

```json
"mcp": {
  "servers": {
    "microsoft-learn": {
      "type": "remote",
      "url": "https://learn.microsoft.com/api/mcp"
    }
  }
}
```

No authentication and no API key. The durable enablement is `learn.microsoft.com:443` in the dotnet kit's runtime allow list, so every sandbox built from this repository's kits can reach the endpoint without a per-session approval. The currently running sandbox predates that kit change and shows an approval-required block; approving the host is only a temporary way to verify before the next `sbx env create`, not a substitute for the kit entry. Do not register it through `dnx`; the vendor exposes it as a remote Streamable HTTP endpoint.

### D7 — One manifest, one script, one thin skill

`.opencode/skills/sources.json` is the single source of truth: repo, pinned ref, mode (`vendor` or `catalog`), include mappings, license path, and a content hash for vendored packs.

`sync-agent-skills.sh`:

- `--check` — recompute vendored hashes and compare the Aspire URL with the manifest; exit non-zero on drift.
- default/`--update` — fetch each repo at the recorded ref (or `--latest` to resolve `main` through `api.github.com`), stage into a temp directory, replace only the managed pack directories, copy licenses, recompute hashes, rewrite the Aspire catalog URL in `opencode.json`, and print `git diff --stat`.
- `--pack <name>` — limit to one pack; `--ref <pack>=<sha>` — pin explicitly.
- Uses only `curl`, `tar`, `python3`, and `mktemp`; no npm or rsync dependency.

`update-agent-skills/SKILL.md` is a thin wrapper that tells the agent the order: run `--check`, update, inspect the upstream diff for renamed/removed skills, verify counts and duplicate IDs, then commit. Rationale: the script is deterministic and CI-usable, while the skill makes the workflow discoverable to the agent without adding a slash command.

## Risks / Trade-offs

- [~100 added skills load their descriptions on every step] → Curated .NET set; skill bodies load only on demand. If noise is a problem, mark low-value IDs with `metadata.opencode/autoinvoke: false`.
- [Vendored content drifts from upstream] → `sources.json` pins plus `--check`; update skill requires reviewing the diff before commit.
- [Aspire catalog needs network on a fresh cache] → `*.githubusercontent.com` is already allowed and the cache lives on the persistent volume; fall back to vendoring if offline sessions become a requirement.
- [Roslyn server rejects Razor/cshtml language IDs] → Verify at apply; restrict to `.cs` and document the limitation.
- [Roslyn via `dotnet dnx` is slow or unavailable on first start] → Verify the command directly and pre-warm the tool cache during apply; sessions continue without diagnostics per spec.
- [Microsoft Learn blocked by sandbox policy] → Kit entry for future sandboxes; current-instance verification needs a host approval. The server is additive, so a block does not affect other packs.
- [.NET agent-dependent skills behave differently] → `code-testing` explicitly executes its workflow inline when `test-engineer` is unavailable; document other agent-oriented skills as agent-assisted only.
- [Upstream renames a skill or changes IDs] → Update procedure includes count/duplicate checks; renamed IDs show up in the diff and in the provenance table.

## Migration Plan

1. Create the sync script, manifest, provenance document, and update skill; run the script to materialize Duende and .NET packs and copy licenses.
2. Convert the two Duende specialists into `.opencode/agents`.
3. Edit `.opencode/opencode.json` for the Aspire catalog, Roslyn LSP, and Microsoft Learn MCP while preserving existing keys.
4. Add `learn.microsoft.com:443` to `sbx/kits/dotnet/dotnet.yaml`.
5. Verify in a session: all packs advertised, one skill per pack loads with its supporting files, both subagents launch, Roslyn starts on a `.cs` edit, and `opencode mcp list` shows the three servers (Learn may require host approval).
6. Commit in reviewable units: vendored packs + provenance/update tooling; agents; config; kit egress.
7. Rollback: revert the commits; no data or external systems to unwind. The Aspire catalog cache can be left in place.

## Open Questions

None. The remaining verification (Razor language ID acceptance, Learn approval) is handled by fallbacks already recorded in Risks.

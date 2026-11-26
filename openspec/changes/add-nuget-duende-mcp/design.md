# Design

## Context

See `proposal.md` for motivation. Current state and constraints that shape the approach:

- The sandbox's dotnet kit (`sbx/kits/dotnet/`) bakes the .NET 10 SDK (10.0.401, dotnetup-managed, `DOTNET_ROOT=/home/agent/.local/share/dotnetup/dotnet`) into the image and stages `/usr/local/bin/dotnet` and `/usr/local/bin/dotnetup` as symlinks at build time. `dnx` ships inside that SDK root but is **not** staged, so it is only reachable by absolute path. `/usr/local/bin` is on every bash `PATH` and on the running OpenCode service's `PATH` (verified via the service process environment).
- `.opencode/opencode.json` currently contains only the `providers` block. OpenCode v2 places MCP servers under `mcp.servers`; a same-named project server entry replaces the global one; config hot-reloads without restart.
- The sandbox runs the workspace in **clone mode**: uncommitted files in the clone are lost on `sbx env rm`; the OpenCode data directory (`~/.local/share/opencode`) is a persistent volume (`volume@1`) that **reattaches** across recreation. The global `~/.config/opencode/opencode.json` is **not** persisted — the opencode-server boot hook rewrites it (merging only the `mcp-gateway` entry) on every boot, so any hand-added global MCP entry is transient by design.
- Kit image changes only take effect after `sbx env rm` + `sbx env create` (rebuild). The NuGet MCP server (`NuGet.Mcp.Server`) was already verified end-to-end in this sandbox (initialize handshake + 6 tools live); the Duende server was verified during planning (see the matrix below).

## Goals / Non-Goals

**Goals:**

- `dnx` resolvable on PATH everywhere in the sandbox, durable in the kit image, with a one-command bridge for the running instance.
- Both MCP servers registered in the committed project config with **portable** commands (no machine-specific absolute paths).
- The Duende SQLite index pinned to the persistent volume, outside the repo, guaranteed-present on any boot (fresh volume included).
- Duende indexing egress documented in the kit's network policy (deny-by-default portability).

**Non-Goals:**

- Duende **Agent Skills** (the `SKILL.md` set from the same Duende page) — separate install, not requested.
- Removing the transient global NuGet entry — it dies with the next sandbox recreation (boot hook owns that file); no task needed.
- Registering MCP servers in the DHI MCP gateway catalogs — empty host-side concern; the project-config route is the durable in-repo mechanism.
- Pinning the tool versions (both servers float to latest on `dnx` download); no local tool manifest is introduced.

## Decisions

### D1 — Expose `dnx` via the kit image, not shell profiles or boot hooks

Add to the existing staging step in `sbx/kits/dotnet/dotnet.dockerfile` (which already symlinks `dotnet` and `dotnetup` into `/out/usr/local/bin`):

```dockerfile
&& test -x /out/home/agent/.local/share/dotnetup/dotnet/dnx \
&& ln -s /home/agent/.local/share/dotnetup/dotnet/dnx /out/usr/local/bin/dnx \
```

Rationale: identical mechanism to `dotnet` ("no shell profile is needed", per the kit's own comments); baked = part of the image digest, so every recreated sandbox has it with no network round trip; `/usr/local/bin` covers bash contexts *and* process spawns (the OpenCode service resolves MCP commands through it). Alternatives rejected: a `PATH` export in `/etc/sandbox-persistent.sh` (covers only bash contexts, adds a moving part), a lifecycle boot hook (the kit deliberately bakes rather than boots; see the dockerfile's own commentary).

### D2 — Project config with portable `dnx` commands

`.opencode/opencode.json` gains `mcp.servers.nuget` and `mcp.servers.duende-mcp`, both launched as plain `dnx <package> …` (resolved via PATH). No absolute sandbox paths in the committed file, so the same config works on any machine with a .NET 10 SDK (Windows installs put `dnx` on PATH via the installer, per the .NET guide). The existing `providers` block stays untouched. Per-server `timeout.startup: 120000` overrides the 30 s default: `dnx` downloads the tool package on first use, and a cold cache after sandbox recreation can exceed 30 s.

### D3 — Duende DB location: `cwd` + the server's documented default, not `--database`

Verified behavior matrix for `Duende.Documentation.Mcp` (shipped version reports `0.0.0.0`), run live during planning:

| `--database` argument | Result |
| --- | --- |
| *(none)* | DB created at `mcp.db` relative to the process working directory |
| relative path, parent exists | **silently ignored** — DB still at `mcp.db` in cwd |
| relative path, parent missing | **silently ignored** — DB still at `mcp.db` in cwd |
| absolute path, parent exists | honored — DB at the given path |
| absolute path, no `--` separator | honored |

So the only honored form (`--database <absolute path>`) is machine-specific and unusable in a committed config, and the relative form silently falls back anyway. The portable way to pin the exact location is `cwd` + the default: `cwd: "~/.local/share/opencode/duende-mcp"` → DB at `~/.local/share/opencode/duende-mcp/mcp.db`. The command stays exactly `["dnx", "Duende.Documentation.Mcp", "--yes", "--"]`, the shape Duende documents and the one verified working.

### D4 — The cwd directory is guaranteed by the opencode-server kit's install hook

A local MCP server cannot start in a nonexistent `cwd`, and a fresh data volume (first boot after `sbx env rm` of a wiped volume) has no `duende-mcp/` subdir. Extend the existing install step in `sbx/kits/opencode-server/opencode-server.yaml` (it already runs `install -d -o agent -g agent -m 0755 /home/agent/.local/share/opencode`) with:

```yaml
- command: install -d -o agent -g agent -m 0755 /home/agent/.local/share/opencode/duende-mcp
  user: "0"
  description: Make the Duende MCP index directory writable by agent
```

That kit already owns the volume's mount root, so this is the pattern-consistent place; it makes the Duende DB location self-healing on any volume state. Alternatives rejected: repo-local DB (clone-mode recreation loses it and pollutes the tree), `/tmp` (lost on every recreation), relying on a manual `mkdir` (breaks silently on fresh volumes).

### D5 — Network allows live in the dotnet kit, next to the NuGet entries

Add to `sbx/kits/dotnet/dotnet.yaml` `network-policy@1` runtime allow: `docs.duendesoftware.com:443`, `duendesoftware.com:443`, `github.com:443`, `codeload.github.com:443` (docs llms.txt, blog RSS, samples zip download). All four were verified reachable through the *current* sandbox policy during planning, so these entries are documentation/portability for future sandboxes, consistent with the kit's deny-by-default philosophy. `github.com` may already be allowed by the provider-keys kit — dedupe during implementation if so; a duplicate allow is harmless.

### D6 — Arg shapes follow vendor docs exactly

- NuGet: `["dnx", "NuGet.Mcp.Server", "--source", "https://api.nuget.org/v3/index.json", "--yes"]` — Microsoft's documented form; verified working on SDK 10.0.401 (note: `--yes` is docs-marked ".NET 11+", but it is tolerated on 10 in practice — both vendors' configs rely on it).
- Duende: `["dnx", "Duende.Documentation.Mcp", "--yes", "--"]` — `--yes` before the `--` separator, nothing after it, since the DB location comes from `cwd` (D3). Verified working.

## Risks / Trade-offs

- [OpenCode's `~` expansion in a local server's `cwd` is documented ("a fixed `cwd`, such as `~`") but not yet exercised by this config] → verified at apply time via `opencode mcp list`; fallback: replace `cwd` with the absolute path `/home/agent/.local/share/opencode/duende-mcp` (sandbox-only, noted in-file) — or, last resort, unset `cwd` and gitignore a workspace-local `mcp.db`.
- [The Duende tool reports version `0.0.0.0`; its `--database` handling and arg quirks may change] → we depend only on the documented default (cwd-relative `mcp.db`); if the tool updates, re-run the D3 matrix before changing the config.
- [First `duende-mcp` connect after recreation re-downloads the tool and starts background indexing; search may be partial briefly] → `timeout.startup` covers the spawn; indexing continues async by design (verified: server answers `initialize` immediately).
- [Kit image changes need a sandbox rebuild] → a same-instance bridge (`sudo ln -s /home/agent/.local/share/dotnetup/dotnet/dnx /usr/local/bin/dnx`) enables verification before the rebuild; it is transient and superseded by D1 at the next `sbx env create`.
- [Windows/other-machine teammates require the .NET 10 SDK for `dnx`] → inherent requirement of both servers; the committed config itself needs no changes.
- [Sdk version floats are not pinned] → `dnx` ships with the SDK; both tool versions float to latest at download time (same trade-off the vendors' own docs make; a local tool manifest could pin later if reproducibility demands it).

## Migration Plan

1. Apply all file changes (tasks 1–6 in `tasks.md`) and the same-instance bridge symlink.
2. Verify in the running instance (dnx on PATH; both servers connect; Duende DB under the persistent volume; `providers` intact).
3. Commit in two units: kit changes; project config.
4. On the host: `sbx env rm` + `sbx env create` — the rebuilt image carries `dnx` and the network entries; the boot hook resets the global config (transient NuGet entry gone by design); the persistent volume reattaches the Duende index.
5. Rollback: revert the two commits; no data or external systems to unwind (the Duende DB dir can simply be deleted).

# Proposal

## Why

AI coding sessions in this repo lack live access to two authoritative sources: current NuGet package metadata (vulnerability remediation, compatible upgrades, package docs) and up-to-date Duende (IdentityServer/BFF/IdentityModel) documentation, samples, and blog content. Microsoft and Duende both ship official MCP servers for exactly this, and both run through `dnx` (.NET 10's `dotnet tool exec` runner) — but the sandbox toolchain installs `dnx` without putting it on `PATH`, and the only existing registration lives in the sandbox's transient global OpenCode config, which is discarded on every `sbx env rm`. There is currently no durable, repo-committed MCP setup at all.

## What Changes

- Expose `dnx` on `PATH` in the sbx `.NET SDK (dotnetup)` kit, mirroring the existing `/usr/local/bin/dotnet` symlink pattern, so the runner is available in every shell and process spawn and survives sandbox recreation (plus a one-command bridge for the currently running sandbox instance).
- Register two MCP servers in the project's `.opencode/opencode.json` (committed with the repo, with portable `dnx` commands and no machine-specific absolute paths) for sandboxes created from this repository's kit configuration:
  - `nuget` — `dnx NuGet.Mcp.Server --source https://api.nuget.org/v3/index.json --yes` (6 tools: vulnerability fixes, upgrade/version plans, latest-version lookups, package docs context, supply-chain review).
  - `duende-mcp` — `dnx Duende.Documentation.Mcp --yes --` with `cwd` pinned to the persistent OpenCode data volume, so its SQLite index (`mcp.db`, the server's documented default relative to its working directory) survives sandbox recreation and never pollutes the repo. The opencode-server kit creates this cwd before OpenCode starts; machines outside the kit-created sandbox workflow must provide the directory separately.
- Add the Duende indexing endpoints (`docs.duendesoftware.com`, `duendesoftware.com`, `codeload.github.com`) to the dotnet kit's network-policy allow list, alongside the existing NuGet entries. The composed OpenSpec kit already grants `github.com`.
- Ensure the Duende index directory exists on every boot by extending the opencode-server kit's install hook (it already owns the data volume's mount root).
- **BREAKING**: nothing. The temporary global NuGet registration is intentionally not carried forward; it disappears with the next sandbox recreation because the global config file is not persisted (the opencode-server boot hook merges only the `mcp-gateway` entry at every boot), and the project config takes over.

## Capabilities

### New Capabilities
- `agent-mcp-servers`: The project's OpenCode setup durably exposes the NuGet and Duende Documentation MCP servers, run through the sandbox toolchain's `dnx`, with the Duende index persisted outside the repo and the required egress allowed.

### Modified Capabilities

(none — the project has no existing specs)

## Impact

- `sbx/kits/dotnet/dotnet.dockerfile` — one symlink + guard in the existing staging step.
- `sbx/kits/dotnet/dotnet.yaml` — three Duende-specific network-policy allow entries; `github.com` is already granted by the OpenSpec kit.
- `sbx/kits/dotnet/dotnet-context.md` — one guidance line.
- `sbx/kits/opencode-server/opencode-server.yaml` — one `install -d` line in the lifecycle install hook.
- `sbx/README.md` — document `dnx`, Duende indexing egress, and the persistent index directory.
- `.opencode/opencode.json` — add `mcp.servers.nuget` and `mcp.servers.duende-mcp` (existing `providers` block untouched).
- Sandbox lifecycle: kit image changes take effect on the next `sbx env rm` + `sbx env create`; a same-instance `/usr/local/bin/dnx` bridge symlink enables verification before that rebuild.
- No application code, APIs, or runtime dependencies change.

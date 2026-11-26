# Tasks

## 1. Expose `dnx` on PATH (dotnet kit + running instance)

- [ ] 1.1 In `sbx/kits/dotnet/dotnet.dockerfile`, extend the existing staging `RUN` (the one that stages `/out/usr/local/bin/dotnet` and `dotnetup`) with `test -x /out/home/agent/.local/share/dotnetup/dotnet/dnx` and `ln -s /home/agent/.local/share/dotnetup/dotnet/dnx /out/usr/local/bin/dnx`. Verify: the diff shows only those two additions inside the existing `&&` chain and `docker build`-style step ordering is unchanged.
- [ ] 1.2 In `sbx/kits/dotnet/dotnet-context.md`, add a guidance line stating `dnx` (.NET 10's `dotnet tool exec` runner) is on PATH alongside `dotnet`. Verify: the file mentions `dnx` and the kit description stays accurate.
- [ ] 1.3 Bridge the currently running sandbox: `sudo ln -s /home/agent/.local/share/dotnetup/dotnet/dnx /usr/local/bin/dnx`. Verify: `bash -lc 'command -v dnx && dnx --version'` succeeds, a non-login `bash -c 'command -v dnx'` succeeds, and `env -i HOME=/home/agent PATH=/usr/local/bin:/usr/bin:/bin dnx --version` succeeds (proves the OpenCode service's spawn PATH resolves it).

## 2. Kit network policy and persistent index directory

- [ ] 2.1 In `sbx/kits/dotnet/dotnet.yaml`, add to the `network-policy@1` runtime allow list (next to the NuGet entries, with a comment naming the Duende MCP indexers): `docs.duendesoftware.com:443`, `duendesoftware.com:443`, `github.com:443`, `codeload.github.com:443`. First grep `sbx/kits/provider-keys/` for existing `github.com` entries and omit an exact duplicate. Verify: the YAML parses and the four (or three, if deduped) entries are present.
- [ ] 2.2 In `sbx/kits/opencode-server/opencode-server.yaml`, add an install step after the existing `install -d ... /home/agent/.local/share/opencode` one: `install -d -o agent -g agent -m 0755 /home/agent/.local/share/opencode/duende-mcp` (user "0", description "Make the Duende MCP index directory writable by agent"). Apply the same command to the running sandbox (`sudo install -d -o agent -g agent -m 0755 /home/agent/.local/share/opencode/duende-mcp`). Verify: the YAML parses, the step mirrors the existing pattern, and `ls -ld /home/agent/.local/share/opencode/duende-mcp` shows an agent-owned 0755 directory.

## 3. Project OpenCode config

- [ ] 3.1 Edit `.opencode/opencode.json` to add `mcp.servers.nuget` (`command: ["dnx", "NuGet.Mcp.Server", "--source", "https://api.nuget.org/v3/index.json", "--yes"]`) and `mcp.servers.duende-mcp` (`command: ["dnx", "Duende.Documentation.Mcp", "--yes", "--"]`, `cwd: "~/.local/share/opencode/duende-mcp"`), each with `timeout: { "startup": 120000 }`. Keep the existing `providers` block byte-identical. Verify: `jq . .opencode/opencode.json` parses, `jq '.providers'` output is unchanged from before the edit, and `opencode mcp list` run from the repo root reports both servers connected (if `duende-mcp` fails to start, its `~` cwd did not expand — replace `cwd` with the absolute path `/home/agent/.local/share/opencode/duende-mcp` per design.md D3 and re-run).

## 4. Integration verification (running instance)

- [ ] 4.1 Confirm both servers expose their tools: `opencode mcp list` shows `✓ nuget` and `✓ duende-mcp` connected, and a session at the repo root sees the six `nuget_*` tools and the Duende search/fetch tools. Verify: the tool lists are non-empty.
- [ ] 4.2 Confirm the Duende index location contract: `ls -la ~/.local/share/opencode/duende-mcp/` shows `mcp.db`, and `git status --porcelain` at the repo root shows no `mcp.db` or other database artifacts. Verify: DB on the persistent volume, repo clean.
- [ ] 4.3 Confirm a live Duende search answers (e.g., ask "What is automatic key management? use Duende" in a session, or call the search tool) and returns content sourced from the indexed docs/blog/samples. Verify: the response cites Duende content rather than failing.

## 5. Commits

- [ ] 5.1 Commit the kit changes: `sbx/kits/dotnet/dotnet.dockerfile`, `sbx/kits/dotnet/dotnet.yaml`, `sbx/kits/dotnet/dotnet-context.md`, `sbx/kits/opencode-server/opencode-server.yaml` with message `sbx: expose dnx on PATH and allow Duende MCP indexing domains`. Verify: `git show --stat HEAD` lists exactly those files.
- [ ] 5.2 Commit the project config with message `opencode: add NuGet and Duende Documentation MCP servers to project config`. Verify: `git show --stat HEAD` lists `.opencode/opencode.json` and the working tree is clean afterwards.

## Workflow follow-up

- On the host, run `sbx env rm` + `sbx env create` to rebuild the image with the kit changes; the transient global NuGet entry in `~/.config/opencode/opencode.json` disappears with it (by design — the boot hook rewrites only the `mcp-gateway` entry).
- After the rebuild, re-verify: `dnx` resolves from the image (not the 1.3 bridge), both MCP servers connect from the project config alone, the Duende index reattaches from the persistent volume, and `sbx policy ls` shows the Duende allow entries.
- Retrieve the commits on the host with `git pull sandbox-zaiware-platform main` (clone mode), or push to `origin` from the sandbox.
- Archive the change after review via the `openspec-archive-change` workflow.

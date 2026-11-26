# Tasks

## 1. Update mechanism

- [x] 1.1 Create `.opencode/skills/sources.json` with `duende` (vendor, ref `aa760867c3e3de167b41dff7e26b4002caf051b1`, include `skills`), `dotnet` (vendor, ref `a273ceb823340c260b796ef49113b399430472a9`, includes for `dotnet`, `dotnet-aspnetcore`, `dotnet-diag`, `dotnet-test`), and `aspire` (catalog, ref `e529b608336cd41c1b1262aa76d00d9e4ef1bff8`, catalog URL). Verify: `jq . .opencode/skills/sources.json` parses and each pack's `ref` matches the expected 40-character SHA.
- [x] 1.2 Create `.opencode/skills/sync-agent-skills.sh` implementing `--check`, `--update` (default), `--latest`, `--pack <name>`, and `--ref <pack>=<sha>`. It must fetch `codeload.github.com` tarballs at the recorded refs into a temp directory, replace only managed pack directories, copy each upstream `LICENSE`, recompute content hashes, and rewrite the Aspire catalog URL in `.opencode/opencode.json`. Verify: `bash -n` passes; `--help` lists every flag; `--check` exits non-zero before vendored content exists.
- [x] 1.3 Create `.opencode/skills/update-agent-skills/SKILL.md` with a description that triggers on "update the agent skills" and instructions to run `--check`, update, review renamed/removed skills, verify counts and duplicate IDs, then commit. Verify: the frontmatter has `name` and `description`; the body references the script flags and the review steps; `opencode api get "/api/skill?location%5Bdirectory%5D=$PWD" | jq -r '.data[].id'` includes `update-agent-skills` after reload.

## 2. Vendor Duende and .NET packs

- [x] 2.1 Run `bash .opencode/skills/sync-agent-skills.sh --pack duende`. Verify: `find .opencode/skills/duende -name SKILL.md | wc -l` is 26; `.opencode/skills/duende/LICENSE` exists; `.opencode/skills/duende/identityserver-configuration/docs` is present; the link `../identityserver-spaces/SKILL.md` from `identityserver-configuration/SKILL.md` resolves to `.opencode/skills/duende/identityserver-spaces/SKILL.md`; `sources.json` records a non-empty hash for the pack.
- [x] 2.2 Run `bash .opencode/skills/sync-agent-skills.sh --pack dotnet`. Verify: `find .opencode/skills/dotnet -name SKILL.md | wc -l` is 38, with per-plugin counts 5 (`dotnet`), 2 (`dotnet-aspnetcore`), 7 (`dotnet-diag`), 24 (`dotnet-test`); `.opencode/skills/dotnet/LICENSE` exists; IDs from excluded plugins (`create-blazor-project`, `convert-to-cpm`, `system-text-json-net11`, `binlog-failure-analysis`, `migrate-dotnet9-to-dotnet10`; `android-tombstone-symbolication` is allowed) are absent except where the plugin is included.
- [x] 2.3 Create `.opencode/skills/README.md` with the source table (repo, ref, mode, destination), included/excluded components, the compatibility review from design.md, license notes, and update commands. Verify: every ref in the README equals `sources.json`; the documented `--check`/`--update` commands run as written; the Aspire row names the pinned catalog URL.
- [x] 2.4 Run `bash .opencode/skills/sync-agent-skills.sh --check` and a duplicate-ID check: `find .opencode/skills -name SKILL.md -printf '%h\n' | xargs -n1 basename | sort | uniq -d`. Verify: `--check` exits 0 and the duplicate check prints nothing.

## 3. Duende specialist subagents

- [x] 3.1 Create `.opencode/agents/identity-server-specialist.md` with the upstream description, `mode: subagent`, and the upstream prompt body; drop Claude-only frontmatter. Verify: `diff` of the body against the upstream agent file (ignoring the frontmatter block) is empty; `opencode debug agents | jq -r '.[] | select(.id=="identity-server-specialist") | .mode'` prints `subagent`.
- [x] 3.2 Create `.opencode/agents/oauth-oidc-specialist.md` the same way. Verify: body diff is empty; `opencode debug agents | jq -r '.[] | select(.id=="oauth-oidc-specialist") | .mode'` prints `subagent`.

## 4. OpenCode project configuration

- [x] 4.1 Add `skills` to `.opencode/opencode.json` with the pinned Aspire catalog URL from `sources.json`. Verify: `jq -r '.skills[0]' .opencode/opencode.json` equals the manifest URL and contains the full commit SHA; `git diff .opencode/opencode.json` shows no change to `providers` or existing `mcp.servers` entries.
- [x] 4.2 Add `mcp.servers.microsoft-learn` as `{"type":"remote","url":"https://learn.microsoft.com/api/mcp"}`. Verify: `jq '.mcp.servers["microsoft-learn"]'` shows the remote type and URL; `opencode mcp list` names the server (a policy block is acceptable until approval, but the entry must not prevent other servers from connecting).

## 5. Sandbox egress and documentation

- [x] 5.1 Add `learn.microsoft.com:443` to the `network-policy@1` runtime allow list in `sbx/kits/dotnet/dotnet.yaml`, next to the other Microsoft endpoints. This is the durable enablement for every kit-created sandbox; host approval is only a pre-rebuild verification shortcut, not a substitute. Verify: the entry appears exactly once at the same indentation as its neighbors; `git diff` shows only the added line; after `sbx env create`, the composed policy contains the entry.
- [x] 5.2 Update `sbx/README.md` to document `learn.microsoft.com` egress and the installed skill packs/update script. Verify: the endpoint is listed in the network section; the update commands match `sync-agent-skills.sh`; the text names the Aspire catalog as the only non-vendored pack.

## 6. Integration verification

- [x] 6.1 List project skills: `opencode api get "/api/skill?location%5Bdirectory%5D=$PWD" | jq -r '.data[].id'`. Verify: the list includes the 26 Duende IDs, `csharp-expert` and `run-tests` from .NET, all 8 Aspire IDs (`aspire`, `aspireify`, `aspire-init`, `aspire-orchestration`, `aspire-deployment`, `aspire-monitoring`, `aspire-terminals`, `aspire-project-v2-migration`), and `update-agent-skills`.
- [x] 6.2 Load one skill per pack in a session and confirm supporting files resolve: `identityserver-configuration` (cite a file from its `docs/`), `csharp-expert` (cite a reference), and `aspire-monitoring` (cite a reference). Verify: each response quotes or summarizes content from the named supporting file, not just the `SKILL.md`.
- [x] 6.3 Launch both specialists through the `subagent` tool with a short prompt. Verify: each subagent returns findings and identifies its specialist role; neither appears in the primary-agent picker.
- [x] 6.4 Check MCP servers: `opencode mcp list` and one Microsoft Learn documentation search. Verify: `nuget` and `duende-mcp` remain connected; `microsoft-learn` connects and returns official Microsoft content, or the result records the pending `learn.microsoft.com` policy approval without marking the other servers failed.
- [x] 6.5 Run the final drift/cleanliness check: `bash .opencode/skills/sync-agent-skills.sh --check`, `git status --porcelain`, and confirm no skill cache or database file appears in the repository. Verify: `--check` exits 0 and `git status` lists only the intended changes.

## 7. Commits

- [x] 7.1 Create a named branch and commit the vendored packs, `sources.json`, `sync-agent-skills.sh`, `update-agent-skills`, and `README.md`. Verify: `git show --stat HEAD` lists only those paths.
- [x] 7.2 Commit the two subagents. Verify: `git show --stat HEAD` lists `.opencode/agents/identity-server-specialist.md` and `.opencode/agents/oauth-oidc-specialist.md`.
- [x] 7.3 Commit `.opencode/opencode.json`, `sbx/kits/dotnet/dotnet.yaml`, and `sbx/README.md`. Verify: `git show --stat HEAD` lists exactly those files and the config diff keeps unrelated keys unchanged.
- [x] 7.4 Confirm the working tree is clean. Verify: `git status --porcelain` prints nothing.

## Workflow follow-up

- Rebuild with `sbx env rm` + `sbx env create` so the kit's `learn.microsoft.com:443` allow entry becomes effective. This is the required durable enablement.
- Approving `learn.microsoft.com` on the host for the current sandbox (`sbx policy approval ls` / `inspect` / `respond`) is optional and only useful to verify the Microsoft Learn MCP before the rebuild.
- After the rebuild, re-verify `opencode mcp list` and one Microsoft Learn tool call from a fresh session.
- Archive the change after review using the `openspec-archive-change` workflow.

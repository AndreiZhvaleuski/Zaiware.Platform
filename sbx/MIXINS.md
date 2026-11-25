# Writing sandbox kit mixins

Playbook for adding or changing a kit under `sbx/kits/`.

Targets sbx v0.50.0 and sandbox-kit schema v3. `sbx env` is experimental; pin
the host CLI version. V3 requires sbx v0.45+; `long-running@1` requires v0.47+;
volume retention across `sbx env rm` requires v0.50+.

## 1. How a kit becomes a sandbox

- Each kit builds alone: a normal build stage, then `FROM scratch` plus
  `COPY --from=build`.
- sbx concatenates the kit layers onto the workload at **create** time, in
  dependency order.
- A directory entry in a kit replaces the base entry at that path, including
  its owner. Different files in the same directory merge.
- Two kits writing the **same file path** collide. Sharing a directory such as
  `/usr/local/bin` is fine.
- The selected agent workload is part of the same composition. Pin its versioned
  workload reference in `sbxenv.yaml`; do not add a mixin that duplicates its
  `provides` entry or replaces its binary paths.
- Kits are create-time. `sbx env run` against an existing sandbox attaches.
  `sbx env exec` attaches and skips lifecycle commands.
- `--detach` starts only the sandbox; it cannot be combined with arguments
  after `--`.

## 2. Install pattern

Every tool gets one `/usr/local/bin/<tool>` symlink. The real files stay at the
vendor default under `/home/agent`. `/usr/local/bin` is the only shared PATH
directory; it contains symlinks, not files.

Build stages set:

```dockerfile
ENV HOME=/home/agent
```

This is build-only. Docker builds run as root with `HOME=/root`; the installers
choose their default paths from `HOME`. Do not set `HOME` as a kit env arg or
in a startup hook: the runtime already has `HOME=/home/agent`.

| Tool | Real install | `/usr/local/bin` | Mutable state |
|---|---|---|---|
| Aspire | `~/.aspire/bin/aspire` + `libhex1binterop.so`, `Aspire.TypeSystem.xml`, `.aspire-install.json` | `aspire` | `~/.aspire/*`; also `~/.cache/Microsoft/DeveloperTools`, `~/.dcp`, `~/.cache/dcp` |
| OpenCode | `~/.opencode/bin/opencode` | `opencode` | `~/.config/opencode` + `~/.local/share/opencode` (persistent kit volume) |
| dotnet | `~/.local/share/dotnetup/dotnet/dotnet` | `dotnet` | `~/.local/share/dotnetup` |
| dotnetup | `~/.dotnetup/dotnetup` | `dotnetup` | `~/.local/share/dotnetup` + writable `~/.dotnetup` |
| OpenSpec | `~/.local/bin/openspec` | `openspec` | `~/.config/openspec` + `~/.local/share/openspec` |

Why the symlink: a PATH entry and an install root are different requirements.
`/usr/local/bin` is root-owned and stable; a tool that writes state beside its
binary, or installs as a directory tree, cannot be reduced to one file. The
symlink bridges the two without editing a shell profile or overriding `PATH`.

OpenSpec is a special case: its default directory `~/.local/bin` is already on
`PATH` and precedes `/usr/local/bin`, so `command -v openspec` may return the
home path. The `/usr/local/bin` link still exists for pattern consistency; both
paths are the same inode.

## 3. Overlay rules

- Build the whole tree under `/out`; create symlinks inside it; finish with
  `COPY --from=build /out /`. This preserves symlinks and ownership.
- Never `COPY` a symlink path as the source. Docker dereferences it and copies
  the target.
- Copy only the install tree and the files the workload lacks. Do not copy a
  whole system directory such as `/usr/local/bin`.
- `/home` stays root-owned; `/home/agent` is uid 1000.
- Keep each tool's build dependencies and checksum/ICU steps.

Ownership:

| Path | Owner |
|---|---|
| `/out/home/agent` | 1000 |
| `~/.aspire`, `~/.aspire/bin` | 1000 |
| `~/.opencode`, `~/.opencode/bin`, `~/.opencode/bin/opencode` | 1000 |
| `~/.dotnetup`, `~/.local/share/dotnetup` | 1000 |
| `~/.local`, `~/.local/share` | 1000 |
| `~/.local/bin` | root |
| `~/.local/bin/openspec` | 1000 |
| `/usr/local/bin/*` symlinks | root |

## 4. Dependencies: `provides` and `requires`

Keep `provides`:

```yaml
provides:
  - "tool@${{ kit.args.toolVersion }}"
```

`requires` is valid v3. It is resolved when the sandbox is created, not during
the per-kit Docker build:

- `requires` entries are `name` or `name <op> version`.
- Resolution is a closed-set check. A missing or unsatisfied provider fails
  sandbox creation.
- sbx orders providers before requirers regardless of the kit list order.
- Kit list order only controls composition of independent kits. It is not a
  dependency mechanism.

This repo does not use `requires` yet because the failure that led to its
removal was never captured as a resolver error. A versioned `requires` had
already worked in `a5657a4`; later "cannot connect" symptoms were traced to
kits that were never fully recreated. Treat `requires` as unverified, not
forbidden.

Probe before adopting it:

1. Copy the kit and the complete environment file to a probe directory.
2. Use a distinct `name:`, an absolute `workspace:`, and a distinct port.
3. Run `sbx env plan`, `sbx env rm --force`, and `sbx env run --detach` with
   the probe file path and `--name`.
4. Test: `provides` only; `requires: [dotnet]` (provider present);
   `requires: [nonexistent]` (must fail creation); `requires: ["dotnet >= 11"]`
   (must fail creation).
5. Adopt only if the satisfied case runs and the unsatisfied cases fail with a
   resolution error.

## 5. Environment

Allowed:

- Tool-specific settings, for example `DCP_IP_VERSION_PREFERENCE=IPv4`,
  `ASPIRE_ALLOW_UNSECURED_TRANSPORT`, `ASPIRE_CLI_TELEMETRY_OPTOUT`.
- `DOTNET_ROOT` from the dotnet kit.

Not allowed:

- `NO_PROXY` / `no_proxy`. The sandbox supplies the correct value. Do not add
  `[::1]`; some clients parse it as a regex character class and lose their
  proxy. Use a tool-specific switch such as `DCP_IP_VERSION_PREFERENCE=IPv4`.
- `PATH`, `HTTP_PROXY`, `HTTPS_PROXY`, or runtime `HOME`.
- `XDG_DATA_HOME` / `XDG_CONFIG_HOME` unless the tool's state path is moved
  with it. Setting them can silently change another kit's volume identity or
  make `dotnetup list` look empty.

Build-stage `HOME` is not a runtime override. Runtime `HOME` is already
`/home/agent`.

Hooks:

- Install hooks are deny-by-default. Name every variable they need in `env:`.
- In sbx, startup hooks receive the full sandbox environment; the spec says
  deny-by-default. Keep the `env:` list for portability.
- Set `user: "0"` explicitly for install hooks that need root.

## 6. Capabilities

| Capability | Use | Notes |
|---|---|---|
| `network-policy@1` | Runtime egress only | Docker builds use the builder's network. Do not duplicate a grant another kit already provides. |
| `volume@1` | State that must survive `sbx env rm` | Identity is sandbox name + kit source + path. Keep `path`, `mode`, and `size` stable. |
| `lifecycle@1` | Processes that run on every boot | Set `user: "0"` explicitly when an install hook needs root. Startup hooks see the sandbox env in sbx. |
| `long-running@1` | Keep the sandbox alive after the last client | Config-less; requires sbx v0.47+. |
| `port@1` | A host-allocated port | Host chooses the port; omitted `family` is `ipv4`. Prefer explicit `ports:` in `sbxenv.yaml` for a stable port. A failed publish aborts creation. |
| `agent-context@1` | Agent instructions | Applied at create. Keep bodies minimal; documentation belongs in this file. |

### Agent context

Every local kit contributes one concise context file. Put only non-obvious,
actionable information in it: paths, preconfigured settings, safe commands, and
things the agent must not do. Installation details, version pins, rationale,
network-rule inventories, and troubleshooting belong in `README.md` or this
file, not in agent context.

An ordinary tool mixin contributes only an instruction body:

```yaml
capabilities:
  - type: com.docker.sandbox/agent-context@1
    config:
      contentFile: ./tool-context.md
```

An agent mixin can also declare `filename` together with an explicit
`directory`:

```yaml
capabilities:
  - type: com.docker.sandbox/agent-context@1
    config:
      filename: AGENTS.md
      directory: /home/agent/.config/opencode
      contentFile: ./opencode-server-context.md
```

OpenCode reads global instructions from
`/home/agent/.config/opencode/AGENTS.md`; `opencode-server` is its agent mixin,
so it declares that explicit destination. The four ordinary tool kits
(`provider-keys`, `dotnet`, `aspire`, `openspec`) use the body-only form.

The frontend stages `contentFile` under `/usr/share/sandbox/kit/<kit>/` and adds
a kit index entry pointing at the staged file. Do not copy the same file in the
Dockerfile as well: the staged copy and the generated index are the capability's
output.

Agent context is applied at create time. `sbx env exec` and attaching to an
existing sandbox do not apply it; recreate the sandbox and verify the generated
`AGENTS.md` and every staged kit context before recording evidence. Do not add
an evidence-log entry until a full build/recreate and verification have actually
passed.

## 7. Phases

| Phase | What it proves | What it cannot prove |
|---|---|---|
| `docker build` | The kit's install steps, layer contents, symlinks, ownership | Other kits, `requires`/`provides`, runtime env |
| `sbx env plan` | A diff against the last applied state: kits, ports, env, capabilities | Resolution, first boot, hooks |
| `sbx env run` (create) | Closed-set resolution, layer composition, install/startup hooks | Client timing |
| Boot hook | Server start and port bind | Anything before the hook |

`sbx env plan` is a review step. It omits unchanged resources and is not the
resolver. `requires`/`provides` are checked at create.

Per-phase checklist:

- Build: descriptor parses; image builds; layer contains only intended files;
  no dangling symlinks; ownership matches the table.
- Plan: expected kit list, ports, env, and capabilities appear.
- Create: full recreate, not `exec`; health endpoint returns authenticated
  `200`; `ps` and the server log show the boot hook ran.
- Smoke: the tool's own version and doctor command pass.

## 8. Template

`sbx/kits/<name>/<name>.yaml`:

```yaml
# syntax=docker/sandbox-kit:3
schemaVersion: "3"
displayName: Tool
description: >-
  What the tool is and where it installs.
sourceUrl: https://example.com/docs
licenses: [MIT]

kind: mixin

provides:
  - "tool@${{ kit.args.toolVersion }}"

args:
  toolVersion:
    default: "1.2.3"
    pattern: '^[0-9]+\.[0-9]+\.[0-9]+$'
    description: Exact tool version
    buildArg: TOOL_VERSION
```

`sbx/kits/<name>/<name>.dockerfile`:

```dockerfile
# syntax=docker/dockerfile:1
FROM debian:trixie-slim AS build

ARG TOOL_VERSION=1.2.3
ENV HOME=/home/agent

RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates curl \
 && rm -rf /var/lib/apt/lists/*

RUN curl -fsSL https://example.com/install.sh -o /tmp/install.sh \
 && sh /tmp/install.sh --version "$TOOL_VERSION" \
 && /home/agent/.local/bin/tool --version \
 && chown 1000:1000 /home/agent /home/agent/.local /home/agent/.local/bin/tool

RUN mkdir -p /out/usr/local/bin /out/home/agent/.local/bin \
 && cp -a /home/agent/.local/bin/tool /out/home/agent/.local/bin/ \
 && ln -s /home/agent/.local/bin/tool /out/usr/local/bin/tool \
 && chown 1000:1000 /out/home/agent /out/home/agent/.local \
 && chown 1000:1000 /out/home/agent/.local/bin/tool

FROM scratch
COPY --from=build /out /
```

Wire it in `sbxenv.yaml` after any kit it needs. List order is composition
order, not dependency resolution.

## 9. Verification

Build through the kit front door:

```sh
docker buildx build -f sbx/kits/<name>/<name>.yaml -t sbx-<name>-test sbx/kits/<name>/
cid=$(docker create --entrypoint /bin/true sbx-<name>-test)
docker cp "$cid:/" - | tar -tvf - | grep -E 'tool|^l|1000/1000 '
docker rm "$cid"
```

Check:

- No dangling symlinks in the layer.
- Expected binaries and libraries are present.
- Ownership matches the table.
- No build-time state directories were baked.

Then:

```powershell
sbx env plan
sbx env rm
sbx env create

$pw = $env:OPENCODE_PASSWORD   # the value stored in ..\zaiware-secrets\server-password
1..30 | ForEach-Object {
  $code = curl.exe -s -o NUL -w "%{http_code}" -u "opencode:$pw" http://127.0.0.1:4096/openapi.json
  Write-Host "attempt $_ -> $code"
  if ($code -eq "200") { break }
  Start-Sleep -Seconds 2
}
Remove-Variable pw
```

Use `/openapi.json`, not `/global/health`: the health path can return the SPA
fallback with `200`. `--detach` returns before the boot hook starts the server.

Run the tool's smoke test inside the sandbox after health is `200`.

## 10. Debugging

| Symptom | First check | Action |
|---|---|---|
| Kit change has no effect | Was the sandbox recreated? | `sbx env rm` then create. `exec` attaches and skips lifecycle. |
| Client cannot connect | `sbx ports`; authenticated `/openapi.json` | 200 means server is up; check host/port/auth |
| No `opencode serve` process | Server log and `ps` | Boot hook has not run or is still starting; recreate or wait |
| Client gets 404/503 | Server start vs first request in `opencode.log` | Startup race; poll health before attaching |
| Tool crashes on startup | Where does it write state? | Move the install root under the agent home |
| Image is huge | `docker history` | A symlink was copied as a source; install to the final path |
| `cmd` is 401 | Does `..\zaiware-secrets\server-password` exist and is it non-empty? | The boot hook fails closed without it; fix the file and recreate |
| Volume did not reattach | Name, kit source, path | Keep all three stable; use sbx v0.50+ |

Do not diagnose from `sbx env exec` alone: it can start a stopped sandbox and
run before the background hook. A kit that only adds a binary cannot change
the server port, hook, or database.

## 11. Evidence log

| Commit | Change | What it proved |
|---|---|---|
| `a5657a4` | First Aspire kit: `/usr/local/bin`, `[::1]`, agent context, network policy, dashboard port | A versioned `requires` was present and the sandbox was created |
| `ddb07ea` | Aspire moved into the agent home | State paths must be writable |
| `a01846e` | `DCP_IP_VERSION_PREFERENCE=IPv4` | Replaces the `[::1]` proxy bypass |
| `bbf6308` | Stopped overriding `NO_PROXY` | The override broke client connectivity |
| `322f6dc` | Removed agent context | It was the only startup-read kit artifact |
| `62b6b36` | Removed dashboard port | Not needed inside the sandbox |
| `5c49c03` | Minimal Aspire kit | Removed several variables at once; did not isolate `requires` |
| `c7f33b6`, `182dc07`, `ed165c4` | Added bare `requires`, removed it, restored `provides` | `requires` remains unverified; `provides` is safe |
| `ed17687`, `df855b4`, `b5e30b9` | OpenSpec kit iterations | Default path, no extra network policy, one owner per file path |

## Related

- `sbx/README.md` — environment layout, ports, volumes, lifecycle.
- `sbxenv.yaml` — kit list, args, ports, bindings.
- `sbx/kits/*/*.yaml` — descriptors; use the working kits as patterns.

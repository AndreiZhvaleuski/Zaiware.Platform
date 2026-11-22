# Sandbox configuration

Everything here configures the Docker Sandbox (`sbx`) that runs OpenCode
against this repository. API keys never live here and never enter the sandbox:
they stay in the host's secret store, and the host-side proxy swaps a
placeholder for the real value on outbound requests.

## Layout

| Path | Role |
|---|---|
| `../sbxenv.yaml` | Environment file: agent, workspace, kits, args, ports, bindings. Must stay at the workspace root. |
| `kits/provider-keys/provider-keys.yaml` | Mixin: network grants + credential injection for OpenCode Go. |
| `kits/dotnet/dotnet.yaml` | Mixin: dotnetup-managed .NET SDK plus the ICU libraries the workload omits. |
| `kits/aspire/aspire.yaml` | Mixin: Aspire CLI (official install script) plus the runtime environment an AppHost needs. |
| `kits/openspec/openspec.yaml` | Mixin: OpenSpec CLI (standalone binary, no Node); declares no network rules. |
| `kits/opencode-server/opencode-server.yaml` | Mixin: keeps the sandbox up after the last client disconnects (`long-running@1`) and persists OpenCode data across recreation (`volume@1`). |
| `../.opencode/opencode.json` | OpenCode config: which env var each provider reads, default permissions. |
| `MIXINS.md` | Playbook for adding or changing a kit: install pattern, template, verification, and the evidence log. |

Split rationale: `opencode.json` is *agent* configuration and belongs in the
repo it configures. `provider-keys` declares *host-side grants* (which domain
may receive which header) and cannot be expressed in OpenCode config.

Before adding or changing a kit, read [`MIXINS.md`](MIXINS.md). It records the
rules the Aspire and OpenSpec kits paid for: one install pattern, no unverified
`requires`, no `NO_PROXY` override, bake into the image, and always verify
after a full recreate.

## First run

```powershell
# Store the API key on the host, once. Prompts for the value.
sbx secret set opencode-go

# One-time: pick a server password and expose it to your shell as
# $env:OPENCODE_PASSWORD. Give the same value to the client you run on
# this machine.
#
# To keep it out of PowerShell history as well, store it OUTSIDE the repo and
# pass --env-args-file instead:
$dir = Join-Path $env:USERPROFILE '.sbx'
New-Item -ItemType Directory -Force $dir | Out-Null
$pw = Read-Host 'OpenCode server password' -MaskInput
Set-Content -Path (Join-Path $dir 'zaiware.args') -Value "serverPassword=$pw"
Remove-Variable pw

# Review, then start the sandbox without attaching: the boot hook starts the
# API server.
sbx env plan
sbx env create --env-arg serverPassword=$env:OPENCODE_PASSWORD
```

If your pinned sbx CLI predates `sbx env create`, use
`sbx env run --detach --env-arg ...` instead — it also boots the sandbox
without attaching.

The args file lives outside the repo on purpose: anything inside the workspace
is readable by the agent. It is not a secret from the agent regardless —
`OPENCODE_SERVER_PASSWORD` is in the sandbox environment because the server
runs there. The password protects the published host port from other host
processes, not the agent from itself.

The password is required so the published port is not an open agent API.
`sbxenv.yaml` maps it to `OPENCODE_SERVER_PASSWORD`, which is the variable
`opencode serve` reads to enable HTTP basic auth. Without it the server starts
without authentication. The client authenticates as `opencode` with this
password.

## OpenCode API server

The server is started by `kits/opencode-server`'s `lifecycle@1` startup hook at
sandbox boot, in the background, so it is not tied to any `sbx` session:

```yaml
startup:
  - command: cd "$WORKSPACE_DIR" && exec opencode serve --hostname 0.0.0.0 --port 4096
    env: [WORKSPACE_DIR, OPENCODE_SERVER_PASSWORD, OPENCODE_API_KEY,
          HTTP_PROXY, HTTPS_PROXY, NO_PROXY]
    background: true
```

Why not `cmd: [serve, ...]` in `sbxenv.yaml`: an attached `sbx env run` is an
exec-style session, and its process is torn down when the session ends, so the
server dies with the terminal. `long-running@1` only stops the sandbox from
auto-stopping; it does not keep a session process alive.

Two details of the hook matter:

- `env:` is still listed. Install hooks are deny-by-default; sbx gives startup
  hooks the full sandbox environment, but the v3 spec says deny-by-default.
  Keep the list for portability and to document what the server needs. Without
  it, a spec-conformant runtime starts without `OPENCODE_SERVER_PASSWORD` (no
  auth) and without the provider credential sentinel.
- `cd "$WORKSPACE_DIR"` sets the server's project root. Startup hooks start in
  the image `WORKDIR`, which is not the mounted workspace.

Start the sandbox without attaching — this boots the sandbox, runs the hook,
and returns your prompt:

```powershell
sbx env create --env-arg serverPassword=$env:OPENCODE_PASSWORD
```

For an in-sandbox TUI while the server runs: `sbx env exec -it -- opencode`.
Day to day, connect the desktop app to the server instead.

Port publishing lives in `sbxenv.yaml` (`ports: 4096 -> 4096`, loopback only).
For an existing sandbox, publish it manually:

```powershell
sbx ports zaiware-platform --publish 4096:4096
```

Client side: the server enforces HTTP basic auth with username `opencode` and
the password you chose. Pass those credentials to the client (`curl -u`, or
`http://opencode:<password>@127.0.0.1:4096` where a client accepts a URL).
`OPENCODE_SERVER_PASSWORD` is the current server-side variable name;
`OPENCODE_PASSWORD` is the pre-rename spelling and may not be read by newer
clients.

Do not bind the published port to a non-loopback host IP without TLS: basic
auth would cross the network in cleartext.

The first `sbx env create` asks you to approve a credential binding for the
service (domain `opencode.ai`). The file pre-declares it, so approval is
normally silent.

## Session data

`kits/opencode-server` declares a `volume@1` at
`/home/agent/.local/share/opencode`. That is where OpenCode keeps the session
database, messages, logs, snapshots, and `auth.json`, so `sbx env rm` followed
by a create reattaches the same data as long as the sandbox name stays
`zaiware-platform`.

- The volume is keyed by **sandbox name + kit source + volume path**, not by
  kit version. For a local source, moving `kits/opencode-server` or
  `sbxenv.yaml` to another directory changes the identity and starts a fresh
  volume.
- Keep `path`, `mode`, and `size` unchanged. A difference refuses allocation
  before the container is replaced.
- The path assumes OpenCode's default data directory (`XDG_DATA_HOME` unset).
  If you set `XDG_DATA_HOME`, move the volume path to match.
- `sbx env rm` keeps the volume by default; deleting it is explicit (`sbx volume ls` on
  the host lists retained volumes, `sbx volume rm` deletes one). A different
  sandbox name starts with a fresh volume. Volume retention across recreation
  requires sbx v0.50.0 or later.
- The first sandbox created with this kit starts with an empty volume.
  History that lived in the old sandbox's home directory is not migrated
  automatically. Export sessions you want to keep before removing that
  sandbox with `opencode session export <session-id>`, then import them after
  recreation with `opencode session import <file> --directory <workspace>`.
- Verify after recreation with `sbx env exec -- opencode session list`.
- The volume is private to the sandbox and not visible in the workspace. If
  you want to inspect or back it up from the host instead, replace the
  `volume@1` entry with `host-mount@1` at the same path and select a host
  directory with `mounts:` in `sbxenv.yaml` (requires sbx v0.50 or later).
  Do not declare both at the same path.

## .NET SDK

`kits/dotnet` bakes a dotnetup-managed SDK into the image at build time. It is
deliberately not a startup hook or an `sbx env exec` install: those repeat the
~200 MB download on every `sbx env rm` / create, and the sandbox is
recreated routinely. A mixin layer is part of the image digest, so every boot
gets the same toolchain with no network round trip.

- Version: the SDK is pinned by `sdkVersion` in `dotnet.yaml` (default
  `10.0.401`), which must match `global.json`; bump both together. The dotnetup
  manager comes from the `preview` quality channel, so it floats per build.
  Record `dotnetup --version` when byte reproducibility matters.
- Location: the dotnetup binary lives at `~/.dotnetup/dotnetup`; the SDK and
  data live at `~/.local/share/dotnetup`. The install is built with
  `HOME=/home/agent`, so the absolute paths in `dotnetup_manifest.json` stay
  valid after the overlay lands on the workload. `dotnetup list`, `update`, and
  `uninstall` work in the sandbox.
- PATH: `/usr/local/bin/dotnet` links to the SDK muxer and
  `/usr/local/bin/dotnetup` links to the manager; `/usr/local/bin` is already on
  the sandbox `PATH`. No shell profile or startup hook is needed.
- Writable state: `~/.dotnetup` must stay writable by uid 1000. dotnetup
  creates an activity lock there on every command, not only for updates.
- `DOTNET_ROOT`: the mixin exports `/home/agent/.local/share/dotnetup/dotnet`.
  `dotnet` resolves through the symlink without it, but an apphost executable
  started directly (Aspire/DCP starts the built AppHost binary, not `dotnet
  run`) looks in `/usr/share/dotnet` and exits with "You must install .NET to
  run this application" when the variable is missing.
- XDG: both install roots assume `XDG_DATA_HOME` is unset. If it is set,
  dotnetup reads a different data directory; the baked SDK and `dotnet` still
  work, but `dotnetup list` will look empty.
- ICU: the opencode workload is a hardened Debian image without `libicu`, and
  the .NET host aborts at startup without it (`Couldn't find a valid ICU
  package`). The Dockerfile copies only the ICU shared libraries from the build
  stage into the overlay; `libssl` and `zlib` come from the workload.
- Egress: the mixin grants NuGet restore (`api.nuget.org`, `*.nuget.org`) and
  the endpoints needed for an in-sandbox SDK install. Add another feed's host
  there if the project uses one.

The build downloads from `aka.ms` and `builds.dotnet.microsoft.com` on the
host, so it needs host network access; the sandbox itself only needs the
runtime grants above.

Verify after the next sandbox creation:

```powershell
sbx env exec -- dotnet --version
sbx env exec -- dotnetup --version
sbx env exec -- dotnetup list
sbx env exec -- bash -lc 'readlink /usr/local/bin/dotnet; readlink /usr/local/bin/dotnetup'
sbx env exec -- bash -lc 'dotnet build Zaiware.Platform.slnx'
```

A Kit change takes effect only when the sandbox is created again. OpenCode's
data directory is a persistent volume (see [Session data](#session-data)), so
later recreations keep it. The first create after this change starts with an
empty volume; export history from the old sandbox first if it matters.

## Aspire

`kits/aspire` installs the Aspire CLI with Aspire's official install script
(`aspire.dev/install.sh`) and bakes it into the image. The script verifies the
release archive's SHA-512; the version is pinned by `aspireVersion`.

- Version: `aspireVersion` (default `13.6.4`) must track the AppHost SDK in
  `Zaiware.Platform.AppHost.csproj` (`Aspire.AppHost.Sdk`) and the Aspire
  packages in `Directory.Packages.props`. Bump them together.
- Layout: the CLI installs at the official default, `~/.aspire/bin/aspire`,
  and `/usr/local/bin/aspire` is a symlink. The installer writes
  `.aspire-install.json` next to the binary; for a script install the CLI keeps
  logs, cache, hives, and versions in the parent (`~/.aspire`), which is
  writable by the agent. Without a recognized sidecar the fallback is
  `ASPIRE_HOME`, then `$HOME/.aspire`. There is no `ASPIRE_CLI_HOME` or XDG
  state variable.
- Dependency: the kit provides `aspire@<version>` but declares no `requires`;
  the dotnet kit must be listed before it in `sbxenv.yaml` and provides
  `DOTNET_ROOT`. `requires` is valid v3 and is resolved at sandbox creation, but
  it is not adopted until the isolated probe in `MIXINS.md` passes. Kit list
  order is composition order, not a dependency resolver.
- Proxy: the mixin does **not** override `NO_PROXY`.
  `DCP_IP_VERSION_PREFERENCE=IPv4` makes DCP use `127.0.0.1` instead of
  `[::1]`, which the sandbox proxy does not bypass. `127.0.0.1` and
  `localhost` are already in the sandbox's `NO_PROXY`. Do not add `[::1]`:
  some clients parse it as a regex and can lose their proxy.
- Transport: `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true` is exported because the
  image ships no trusted ASP.NET Core HTTPS development certificate. Use the
  `http` launch profile; the sandbox publishes loopback ports only.
- Telemetry: `ASPIRE_CLI_TELEMETRY_OPTOUT=true`.
- Updates: the CLI is image-managed. Bump `aspireVersion` and recreate. An
  in-sandbox `aspire update` changes only that sandbox and is lost on the next
  recreate, so it is not part of the workflow.
- Uninstall: delete `~/.aspire` and `/usr/local/bin/aspire`; the install script
  has no uninstall command.

The build downloads the CLI from `aspire.dev` and the matching GitHub release
on the host, so it needs host network access. The sandbox itself needs no extra
grants from this kit: the dotnet kit grants NuGet restore, and the Docker
daemon pulls the dashboard and database images.

Verify after the next sandbox creation:

```powershell
sbx env exec -- aspire --version
sbx env exec -- bash -lc 'cd "$WORKSPACE_DIR" && aspire doctor'
```

`aspire doctor` may warn that the CLI is out of date, and about the missing
trusted developer certificate/`certutil`. Both are expected: updates are kit
rebuilds, and the image ships no trusted ASP.NET Core development certificate.

Run the AppHost from the workspace root:

```powershell
# Background (use `aspire run` for foreground interactively).
sbx env exec -- bash -lc 'cd "$WORKSPACE_DIR" && aspire start --non-interactive --launch-profile http'

sbx env exec -- aspire ps
sbx env exec -- aspire describe
sbx env exec -- aspire logs identity-server
sbx env exec -- aspire stop
```

DCP uses the Docker daemon exposed in the sandbox, so `postgres` and `pgAdmin`
appear in `docker ps`; project resources run as local processes. Everything
stays inside the sandbox.

The `http` launch profile runs the dashboard inside the sandbox at
`http://zaiware-platform.dev.localhost:15154`. It is deliberately not published
to the host. Use the Aspire CLI from inside the sandbox for inspection:

```powershell
sbx env exec -- aspire describe
sbx env exec -- aspire logs identity-server
sbx env exec -- aspire otel
```

If you need the dashboard UI, add the `15154` mapping under `ports:` in
`sbxenv.yaml` temporarily and open the printed `/login?t=...` URL; the
dashboard has no authentication beyond that browser token, so keep it on
loopback.

A Kit change takes effect only when the sandbox is created again
(`sbx env rm && sbx env create`); the OpenCode data volume keeps its sessions.

## OpenSpec

`kits/openspec` installs the OpenSpec CLI with OpenSpec's official standalone
installer (`openspec.dev/cli/install.sh`) and bakes it into the image. The
release is a native binary, so no Node.js runtime is involved; the installer
also verifies the archive against the release's `SHA256SUMS`. The version is
pinned by `openspecVersion`.

- Location: the binary installs at the official default,
  `~/.local/bin/openspec`, and `/usr/local/bin/openspec` is a symlink. The
  `~/.local/bin` directory stays root-owned; the binary is uid 1000. Because
  `~/.local/bin` precedes `/usr/local/bin` on `PATH`, `command -v openspec` may
  return the home path; both paths are the same inode.
- Why baked: the sandbox is recreated routinely, and an install performed at
  runtime (or by hand) lives in the sandbox's writable layer, so it disappears
  on `sbx env rm`. A mixin layer is part of the image digest.
- Updates: bump `openspecVersion` and recreate. An in-sandbox `openspec update`
  or `openspec install` changes only that sandbox and is lost on the next
  recreate. The mixin declares no network rules: the OpenCode workload already
  allows `github.com`, `api.github.com`, and `*.githubusercontent.com`.
- No state volume: the artifacts that matter (`openspec/` and the
  `openspec-*` skills) are written into the workspace by `openspec init`, not
  into the sandbox. `~/.config/openspec` and `~/.local/share/openspec` hold
  only disposable per-machine state.
- If the client cannot connect after adding this Kit, confirm the sandbox was
  recreated and give the background startup hook time to finish. `sbx env create`
  returns before `opencode serve` is listening; poll the authenticated
  API before starting the client. `sbx env exec` against a stopped sandbox
  starts it and runs the command immediately, so a `ps` right after can run
  before the hook has launched the server. A boot that runs the hook writes a
  new `cli starting ... "serve"` line to
  `~/.local/share/opencode/log/opencode.log`.

The Kit only supplies the CLI binary. The project side is initialized at the
repo root with:

```powershell
openspec init --tools opencode
```

That writes:

```text
openspec/config.yaml                       project schema and context
openspec/specs/                            current specs
openspec/changes/                          in-motion changes (archive/ inside)
.agents/skills/openspec-*/                 the six workflow skills
```

These are committed with the repository, so a fresh checkout has the workflow
skills. Re-run `openspec init --tools opencode` after upgrading OpenSpec to
refresh them.

Verify after the next sandbox creation:

```powershell
sbx env exec -- openspec --version
sbx env exec -- bash -lc 'command -v openspec; readlink /usr/local/bin/openspec'
sbx env exec -- bash -lc 'cd "$WORKSPACE_DIR" && openspec list'
```

`openspec --version` prints the bare version (for example `2.0.0`);
`openspec version` prints the full `OpenSpec 2.0.0 (standalone)` line after a
first-run telemetry note. Restart OpenCode after a recreation so it reloads
`.agents/skills`; then `/openspec-propose` (or asking the agent to use the
matching skill) is available.

## Verify

```powershell
# A placeholder, never the real key.
sbx env exec -- bash -c 'env | grep -E "OPENCODE_API_KEY"'

# Must return 200. A 401 means the placeholder reached the API unchanged.
sbx env exec -- bash -c 'curl -s -o /dev/null -w "go %{http_code}\n" \
  -H "Authorization: Bearer $OPENCODE_API_KEY" https://opencode.ai/inference/go/openai/v1/models'

# No stored credentials inside the server: placeholders only.
sbx env exec -- bash -c 'env | grep -E "OPENCODE_API_KEY" | cut -d= -f1'

# The API server is listening and enforces the password:
# 200 with credentials, 401 without (or with the wrong password).
# Use /openapi.json: /global/health can return the SPA fallback with 200.
$pw = $env:OPENCODE_PASSWORD
curl.exe -s -o NUL -w "auth %{http_code}`n" -u "opencode:$pw" http://127.0.0.1:4096/openapi.json
curl.exe -s -o NUL -w "anon %{http_code}`n" http://127.0.0.1:4096/openapi.json
Remove-Variable pw
```

If `anon` is not `401`, the password did not reach the server: the startup
hook's `env:` list must include `OPENCODE_SERVER_PASSWORD`, and the value must
be supplied with `--env-arg`/`--env-args-file` on every `sbx env` invocation.

Never run `/connect` in this sandbox — that stores the real key in the
persisted OpenCode database (`~/.local/share/opencode/opencode.db`) as plaintext.

## Models

| Provider id | Example model | Auth |
|---|---|---|
| `opencode-go` | `opencode-go/kimi-k3`, `opencode-go/deepseek-v4.1-flash` | `OPENCODE_API_KEY` |
| `opencode` | `opencode/deepseek-v4.1-flash-free` | none (free tier) |

The native `deepseek` provider was dropped: OpenCode Go already serves the
DeepSeek models this project uses (`opencode-go/deepseek-v4.1-flash`), so one
credential covers everything.

## Lifecycle

- Secrets are host-scoped, so recreating the sandbox keeps them: `sbx env rm && sbx env create`.
- Rotating a key: `sbx secret set opencode-go` again. Placeholders are stable, so no restart is needed.
- Session history is stored in a kit volume (`~/.local/share/opencode`), so `sbx env rm` followed by a create keeps sessions when the sandbox name, kit source, and volume path stay the same. A different name or a moved kit directory starts empty.
- Do not run `sbx reset` — it deletes all stored host secrets.

## Protecting this directory

`sbxenv.yaml` is mounted read-only because it sits at the workspace root.
`sbx/` is not: it is an ordinary part of the workspace, so the agent can edit
the kits that a later create builds and executes. Make it a read-only
additional workspace in `sbxenv.yaml` (this file is read-only in the sandbox, so
apply the edit on the host):

```yaml
additionalWorkspaces:
  - path: ./sbx
    readOnly: true
```

Additional workspaces mount after the primary workspace, so the `:ro` mount
shadows `<workspace>/sbx`. Kit sources remain editable on the host and
read-only inside the sandbox. Nested read-only mounts are supported; they no
longer unmount on host file changes as of sbx v0.42.0.

With clone mode (`workspace: {path: ., clone: true}`) the sandbox edits a
private git clone, so the agent cannot influence what the host executes in
the first place; this section matters if you switch to direct mounting.

Trade-off: the whole directory, including this README, is read-only in the
sandbox, so git operations that would modify `sbx/` (checkout to a branch that
touches it, stash, clean) fail there. For a boundary that survives that, move
`kits/` outside the workspace (for example
`%USERPROFILE%\.sbx\zaiware\kits`) and reference the absolute path from
`kits:`, or publish the kits to a registry and pin them by digest.

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
| `kits/provider-keys/provider-keys-context.md` | Agent context: proxy-managed key placeholder and safe credential handling. |
| `kits/dotnet/dotnet.yaml` | Mixin: dotnetup-managed .NET SDK plus the ICU libraries the workload omits. |
| `kits/dotnet/dotnet-context.md` | Agent context: preinstalled SDK and configured `DOTNET_ROOT`. |
| `kits/aspire/aspire.yaml` | Mixin: Aspire CLI (official install script) plus the runtime environment an AppHost needs. |
| `kits/aspire/aspire-context.md` | Agent context: HTTP launch profile, configured environment, and Aspire commands. |
| `kits/openspec/openspec.yaml` | Mixin: OpenSpec CLI (standalone binary, no Node) and its GitHub runtime access. |
| `kits/openspec/openspec-context.md` | Agent context: project artifacts, workflow skills, and init guidance. |
| [`docker.io/sbx/playwright:1.61.1`](#playwright) | [Published Playwright mixin](#playwright): `playwright`, `@playwright/test`, headless Chromium, shared browser path, and scoped network grants. |
| `kits/opencode-server/opencode-server.yaml` | Mixin: pinned OpenCode binary, MCP/server startup, keep-alive (`long-running@1`), persistent OpenCode data (`volume@1`), and the OpenCode `AGENTS.md` context profile (`agent-context@1`). |
| `kits/opencode-server/opencode-server-context.md` | Injected global `AGENTS.md` profile: server-mode, clone-only workspace, and Git handoff workflow. |
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

# One-time: create the server-password file OUTSIDE the repo, as a sibling of
# the project. Give the same value to the client you run on this machine
# (e.g. as $env:OPENCODE_PASSWORD).
$dir = "C:\Users\WRNGx3\Projects\zaiware-secrets"
New-Item -ItemType Directory -Force $dir | Out-Null
Read-Host 'OpenCode server password' -MaskInput |
  Set-Content -NoNewline "$dir\server-password"

# Review, then start the sandbox without attaching: the boot hook starts the
# API server.
sbx env plan
sbx env create
```

No arguments are needed on any `sbx env` command: the password never passes
through the CLI, so it never appears in PowerShell history or the environment
plan. `sbxenv.yaml` mounts `..\zaiware-secrets` read-only, and the
opencode-server boot hook reads `server-password` from it at every boot.

The directory lives outside the repo on purpose: it must not be committed, and
in clone mode untracked files never reach the sandbox anyway — only this mount
does. It is not a secret from the agent regardless: the mount is readable inside
the sandbox because the server runs there. The password protects the published
host port from other host processes, not the agent from itself.

The password is required so the published port is not an open agent API. The
boot hook fails if the file is missing or empty, so the server refuses to start
rather than serving an unauthenticated port. The client authenticates as
`opencode` with this password.

## OpenCode API server

The only workload is `docker.io/sbx/shell:1.0.0`, selected for its Docker
Engine, Compose, Buildx, Git, and Debian/glibc runtime. `agent: shell` selects
its registered name. The `opencode-server` mixin adds OpenCode **2.4.1**, MCP
gateway registration, the headless API server, and persistent state; no
interactive OpenCode workload or ACP/TUI integration is needed.

The mixin installs the pinned binary with OpenCode's official v2 installer,
which selects the correct glibc build. The upstream
`ghcr.io/anomalyco/opencode:2.4.1` image is deliberately not used: it is an
Alpine/musl runtime, not an sbx kit, and its binary is incompatible with this
Debian/glibc composition. Do not combine this mixin with `sbx/opencode`, which
already provides the same feature and binary paths.

To upgrade, bump `opencodeVersion` and the matching `OPENCODE_VERSION`
build-arg default, then recreate the sandbox. After recreation,
`sbx env exec -- opencode --version` must print `opencode v2.4.1`.

The server is started by `kits/opencode-server`'s `lifecycle@1` startup hook at
sandbox boot, in the background, so it is not tied to any `sbx` session:

```yaml
startup:
  - command: cd "$WORKSPACE_DIR" && test -s ../zaiware-secrets/server-password \
      && export OPENCODE_SERVER_PASSWORD="$(tr -d '\r\n' < ../zaiware-secrets/server-password)" \
      && exec opencode serve --hostname 0.0.0.0 --port 4096
    env: [WORKSPACE_DIR, OPENCODE_API_KEY, HTTP_PROXY, HTTPS_PROXY, NO_PROXY]
    background: true
```

Why not `cmd: [serve, ...]` in `sbxenv.yaml`: an attached `sbx env run` is an
exec-style session, and its process is torn down when the session ends, so the
server dies with the terminal. `long-running@1` only stops the sandbox from
auto-stopping; it does not keep a session process alive.

Three details of the hook matter:

- `env:` is still listed. Install hooks are deny-by-default; sbx gives startup
  hooks the full sandbox environment, but the v3 spec says deny-by-default.
  Keep the list for portability and to document what the server needs. Without
  it, a spec-conformant runtime starts without the provider credential sentinel.
- `cd "$WORKSPACE_DIR"` sets the server's project root. Startup hooks start in
  the image `WORKDIR`, which is not the mounted workspace.
- The password file is checked with `test -s` before the server starts: if it
  is missing or empty the hook fails, so the port is never served
  unauthenticated. `tr -d '\r\n'` strips the line ending Windows editors leave
  behind.

Normal operation always uses the boot-managed server started by
`sbx env create`. Start the sandbox without attaching — this boots the sandbox,
runs the hook, and returns your prompt:

```powershell
sbx env create
```

An attached `sbx env run` is an interactive exec session, not the normal server
startup path. For an in-sandbox TUI while the server runs:
`sbx env exec -it -- opencode`. Day to day, connect the desktop app to the server
instead.

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

The first `sbx env create` asks you to approve the additional workspace mount
and a credential binding for the service (domain `opencode.ai`). The binding is
pre-declared, so approval is normally silent; the mount appears in the plan.

## Agent context and Git handoff

Each selected kit contributes focused agent context through `agent-context@1`:

- `provider-keys`: `OPENCODE_API_KEY` is a proxy-managed placeholder; never
  persist a real key or run `/connect`.
- `dotnet`: `dotnet` and `dotnetup` are preinstalled; use the baked SDK with the
  configured `DOTNET_ROOT`.
- `aspire`: use the `http` launch profile and the preconfigured environment;
  start, inspect, and stop through the Aspire CLI.
- `openspec`: use the workflow skills in `.agents/skills/openspec-*` and keep
  project artifacts under `openspec/`.
- `playwright`: `playwright` and `@playwright/test` are global; only headless
  Chromium is provisioned. Preserve `/opt/ms-playwright`; version `1.61.1`
  exports `PLAYWRIGHT_BROWSERS_PATH` and `NODE_PATH` only for login shells, so
  use `bash -lc` or set the browser path explicitly in non-login shells. Grant
  external sites separately.
- `opencode-server`: use server mode, edit only the clone, and commit completed
  work for host retrieval.

The five ordinary tool kits (`provider-keys`, `dotnet`, `aspire`, `openspec`, and
the published `playwright` kit) use the `agent-context@1` body-only form: their
config contains only `contentFile`. The frontend stages each body under
`/usr/share/sandbox/kit/<kit>/` and adds a kit index entry; ordinary bodies are
not written as global instruction files. In particular, the published kit stages
`/usr/share/sandbox/kit/playwright/playwright-context.md`. `opencode-server`
additionally owns the explicit global profile: its `agent-context@1` entry writes
`/home/agent/.config/opencode/AGENTS.md` from
[`opencode-server-context.md`](kits/opencode-server/opencode-server-context.md).
The mechanism does not create a repository-root `AGENTS.md`.

The generated profile requires the agent to:

- use the boot-managed OpenCode server started by `sbx env create`;
- edit only the private Git clone at `$WORKSPACE_DIR`, never the read-only host
  source at `/run/sandbox/source`;
- commit completed file-changing tasks in the sandbox and report the branch and
  commit hash.

The Docker sbx remote `sandbox-zaiware-platform` exists on the **host**: it lets
the host fetch commits from the sandbox. It is not available inside the sandbox,
so the agent must never run `git push sandbox-zaiware-platform`. After the agent
reports a branch and commit, retrieve the work from the host repository with the
corresponding branch checked out:

```powershell
git pull sandbox-zaiware-platform <branch>
```

Do not remove or recreate the sandbox before retrieving commits that exist only
in its private clone; `sbx env rm` deletes that clone.

Changing the kit context requires a full sandbox recreation before it takes
effect. `sbx env exec` and attaching to the existing sandbox do not apply new
agent context.

## Session data

`kits/opencode-server` declares a `volume@1` at
`/home/agent/.local/share/opencode`. That is where OpenCode keeps the session
database, messages, logs, snapshots, and `auth.json`, so `sbx env rm` followed
by a create reattaches the same data as long as the sandbox name stays
`zaiware-platform`. The install hook also creates the agent-owned
`duende-mcp/` subdirectory where the Duende Documentation MCP server keeps its
persistent `mcp.db` index.

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
- PATH: `/usr/local/bin/dotnet` links to the SDK muxer,
  `/usr/local/bin/dotnetup` links to the manager, and `/usr/local/bin/dnx`
  links to the .NET 10 tool runner; `/usr/local/bin` is already on the sandbox
  `PATH`. No shell profile or startup hook is needed.
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
- ICU: the shell workload is a hardened Debian image without `libicu`, and
  the .NET host aborts at startup without it (`Couldn't find a valid ICU
  package`). The Dockerfile copies only the ICU shared libraries from the build
  stage into the overlay; `libssl` and `zlib` come from the workload.
- Egress: the mixin grants NuGet restore (`api.nuget.org`, `*.nuget.org`),
  the endpoints needed for an in-sandbox SDK install, and the Duende MCP
  indexers (`docs.duendesoftware.com`, `duendesoftware.com`, and
  `codeload.github.com`). The composed OpenSpec kit already grants
  `github.com` for sample-source links. Add another feed's host there if the
  project uses one.

The build downloads from `aka.ms` and `builds.dotnet.microsoft.com` on the
host, so it needs host network access; the sandbox itself only needs the
runtime grants above.

Verify after the next sandbox creation:

```powershell
sbx env exec -- dotnet --version
sbx env exec -- dotnetup --version
sbx env exec -- dnx --help
sbx env exec -- dotnetup list
sbx env exec -- bash -lc 'readlink /usr/local/bin/dotnet; readlink /usr/local/bin/dotnetup; readlink /usr/local/bin/dnx'
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
  recreate. The mixin grants `github.com`, `api.github.com`, and
  `*.githubusercontent.com` for runtime updates and GitHub repository
  operations; its build-time installer uses the host network instead.
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

## Playwright

The environment pins Docker's published, verified
`docker.io/sbx/playwright:1.61.1` mixin. It provides the global `playwright`
CLI, `@playwright/test`, and headless Chromium for the agent without requiring a
project-local Node install. The kit adds a ~292 MB amd64 image layer (~327 MB on
arm64), and every create re-runs its install hook to add Node when needed and
install Chromium's native system libraries. Only the image pull is cached after
the first create; the apt work still runs on each recreation.

The shared browser directory is `/opt/ms-playwright`, and it is writable by the
agent. Version `1.61.1` exports `PLAYWRIGHT_BROWSERS_PATH` and `NODE_PATH` from
`/etc/profile.d/playwright-env.sh`, so login shells receive them but non-login
shells do not. That includes bare `sbx env exec` commands and the agent's default
non-login Bash shell: `BASH_ENV` points to an empty
`/etc/sandbox-persistent.sh`, which does not source `profile.d`. Use `bash -lc`
when a command needs the login-shell environment, or set
`PLAYWRIGHT_BROWSERS_PATH=/opt/ms-playwright` explicitly for browser commands.
Do not duplicate these variables in `sbxenv.yaml` or another local kit.

A project-local Playwright version must use a matching browser. Use the global
`playwright install chromium` command, or a project-local
`./node_modules/.bin/playwright` after installing that project's dependencies;
the browser CDN is allowed at runtime. This `1.61.1` kit installs Node but not
npm/npx, so `npx playwright ...` is not available unless npm is installed
separately. The published context's `npx` examples do not apply to this pin.

Only headless Chromium and its headless shell are provisioned. There is no
display server, so headed mode, UI mode, and codegen are unsupported. Headless
screenshots, PDFs, traces, and videos work; Firefox and WebKit are not included.

The kit's install policy allows the Debian/DHI and related package repositories
needed by `playwright install-deps chromium`: `deb.debian.org`, `dhi.io`,
`archive.ubuntu.com`, `security.ubuntu.com`, `ports.ubuntu.com`,
`download.docker.com`, and `cli.github.com`. Its runtime policy allows
`cdn.playwright.dev`, `playwright.download.prss.microsoft.com`, and
`storage.googleapis.com` for browser assets. `localhost` continues to work
through the sandbox's normal proxy bypass. These grants do **not** give access
to arbitrary sites under test: grant each external site's domain separately,
and never add a wildcard `NO_PROXY` override.

Kit changes are create-time only. After changing the reference, run
`sbx env plan`, retrieve any sandbox-only commits on the host, then run
`sbx env rm` and `sbx env create`. `sbx env exec` or attaching does not compose
the kit or run its install hook.

Verify after recreation:

```powershell
sbx env exec -- playwright --version
sbx env exec -- bash -lc 'node -e "require(\"@playwright/test\"); console.log(\"resolved\")"'
sbx env exec -- bash -lc 'echo "$PLAYWRIGHT_BROWSERS_PATH"; test -w /opt/ms-playwright && echo writable'
# Known 1.61.1 limitation: both variables are unset in a non-login shell.
sbx env exec -- bash -c 'printf "non-login PBP=%s NODE_PATH=%s\n" "${PLAYWRIGHT_BROWSERS_PATH:-unset}" "${NODE_PATH:-unset}"'
# Serve localhost and render it with the shared browser path explicitly set.
sbx env exec -- bash -c 'mkdir -p /tmp/pw-smoke-site; printf "<title>Playwright smoke</title><h1>headless Chromium</h1>\n" > /tmp/pw-smoke-site/index.html; python3 -m http.server 8765 --bind 127.0.0.1 --directory /tmp/pw-smoke-site >/tmp/pw-smoke-server.log 2>&1 & server=$!; trap "kill $server" EXIT; sleep 0.5; PLAYWRIGHT_BROWSERS_PATH=/opt/ms-playwright playwright screenshot --wait-for-timeout=1000 http://127.0.0.1:8765/ /tmp/pw-smoke.png && test -s /tmp/pw-smoke.png && head -c 8 /tmp/pw-smoke.png | od -An -tx1 | grep -q "89 50 4e 47"'
sbx env exec -- test -s /usr/share/sandbox/kit/playwright/playwright-context.md
sbx env exec -- bash -c 'ctx=/usr/share/sandbox/kit/playwright/playwright-context.md; grep -q "@playwright/test" "$ctx" && grep -q "/opt/ms-playwright" "$ctx" && grep -qi "headless" "$ctx" && grep -qiE "external|policy" "$ctx"'
sbx env exec -- grep -q playwright /home/agent/.config/opencode/AGENTS.md
# After confirming the domain is absent from sbx policy ls, this must fail with
# a policy/proxy error. Substitute another ungranted domain if example.com is allowed.
sbx env exec -- bash -c 'PLAYWRIGHT_BROWSERS_PATH=/opt/ms-playwright playwright screenshot --wait-for-timeout=1000 https://example.com /tmp/pw-denied.png'
# No display server: headed mode must fail rather than opening a window.
sbx env exec -- bash -c 'PLAYWRIGHT_BROWSERS_PATH=/opt/ms-playwright playwright screenshot --headed file:///tmp/pw-smoke-site/index.html /tmp/pw-headed.png'
```

The version must be `1.61.1`, the module must resolve in a login shell, the
browser path must be `/opt/ms-playwright`, and the localhost smoke image must be
a non-empty PNG whose first bytes are `89 50 4e 47`. The non-login probe should
report both variables as `unset`; browser commands must set the shared path
explicitly or run in a login shell.

Do not replace the pin with `latest`. A future move to
`docker.io/sbx/playwright:1.65.1-0` is a separate upgrade. Its `version` setting
is a build-phase argument baked into the published image, so consumers select
the release by tag rather than passing `args.version`. Use the bare reference
below in an isolated probe:

```yaml
- docker.io/sbx/playwright:1.65.1-0
```

That descriptor declares `requires: [deb/apt, deb/bash, deb/coreutils, deb/grep]`,
which this environment has not probed. It also changes the packaging to include
npm and expose the environment to non-login Bash shells. Verify the dependency
contract in an isolated sandbox before changing the pin, then recreate and
rerun the checks.

## Verify

```powershell
# A placeholder, never the real key.
sbx env exec -- bash -c 'env | grep -E "OPENCODE_API_KEY"'

# Must return 200. A 401 means the placeholder reached the API unchanged.
sbx env exec -- bash -c 'curl -s -o /dev/null -w "go %{http_code}\n" \
  -H "Authorization: Bearer $OPENCODE_API_KEY" https://opencode.ai/inference/go/openai/v1/models'

# No stored credentials inside the server: placeholders only.
sbx env exec -- bash -c 'env | grep -E "OPENCODE_API_KEY" | cut -d= -f1'

# The password file reached the sandbox:
sbx env exec -- bash -lc 'test -s "$WORKSPACE_DIR/../zaiware-secrets/server-password" && echo password file present'

# The API server is listening and enforces the password:
# 200 with credentials, 401 without (or with the wrong password).
# Use /openapi.json: /global/health can return the SPA fallback with 200.
$pw = $env:OPENCODE_PASSWORD   # or: Get-Content ..\zaiware-secrets\server-password -Raw
curl.exe -s -o NUL -w "auth %{http_code}`n" -u "opencode:$pw" http://127.0.0.1:4096/openapi.json
curl.exe -s -o NUL -w "anon %{http_code}`n" http://127.0.0.1:4096/openapi.json
Remove-Variable pw

# Every selected kit's context is staged, and the generated profile indexes it:
sbx env exec -- bash -lc 'for k in provider-keys dotnet aspire openspec playwright opencode-server; do test -s "/usr/share/sandbox/kit/$k/$k-context.md" || { echo "missing $k"; exit 1; }; done'
sbx env exec -- bash -lc 'for k in provider-keys dotnet aspire openspec playwright opencode-server; do grep -q "$k" /home/agent/.config/opencode/AGENTS.md || { echo "missing index $k"; exit 1; }; done'
sbx env exec -- test -s /home/agent/.config/opencode/AGENTS.md
sbx env exec -- cat /home/agent/.config/opencode/AGENTS.md
```

The `AGENTS.md` output must contain a kit index/reference for `provider-keys`,
`dotnet`, `aspire`, `openspec`, `playwright`, and `opencode-server`. The staged
contexts must contain the credential, SDK, Aspire, OpenSpec, headless-browser,
and server/clone/Git-handoff guidance respectively. This mechanism does not
create a repository-root `AGENTS.md`.

If `anon` is not `401`, the server either did not start or has no password.
Check that `..\zaiware-secrets\server-password` exists on the host and is
non-empty — the boot hook fails closed on a missing file, so the port is never
served unauthenticated.

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

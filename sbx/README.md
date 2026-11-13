# Sandbox configuration

Everything here configures the Docker Sandbox (`sbx`) that runs OpenCode
against this repository. API keys never live here and never enter the sandbox:
they stay in the host's secret store, and the host-side proxy swaps a
placeholder for the real value on outbound requests.

## Layout

| Path | Role |
|---|---|
| `../sbxenv.yaml` | Environment file: agent, workspace, kits, args, ports, bindings. Must stay at the workspace root. |
| `kits/provider-keys/provider-keys.yaml` | Mixin: network grants + credential injection for DeepSeek and OpenCode Go. |
| `kits/dotnet/dotnet.yaml` | Mixin: dotnetup-managed .NET SDK plus the ICU libraries the workload omits. |
| `kits/opencode-server/opencode-server.yaml` | Mixin: keeps the sandbox up after the last client disconnects (`long-running@1`). |
| `../.opencode/opencode.json` | OpenCode config: which env var each provider reads, default permissions. |

Split rationale: `opencode.json` is *agent* configuration and belongs in the
repo it configures. `provider-keys` declares *host-side grants* (which domain
may receive which header) and cannot be expressed in OpenCode config.

## First run

```powershell
# Store the API keys on the host, once. Each prompts for the value.
sbx secret set deepseek
sbx secret set opencode-go

# One-time: pick a server password and keep it OUTSIDE the mounted workspace.
# Give the same value to the client you run on this machine.
$dir = Join-Path $env:USERPROFILE '.sbx'
New-Item -ItemType Directory -Force $dir | Out-Null
$pw = Read-Host 'OpenCode server password' -MaskInput
Set-Content -Path (Join-Path $dir 'zaiware.args') -Value "serverPassword=$pw"
Remove-Variable pw

# Review, then start the sandbox without attaching: the boot hook starts the
# API server.
sbx env plan
sbx env run --detach --env-args-file "$env:USERPROFILE\.sbx\zaiware.args"
```

The args file lives outside the workspace on purpose: anything inside the repo
is mounted into the sandbox and readable by the agent. It is not a secret from
the agent regardless — `OPENCODE_SERVER_PASSWORD` is in the sandbox environment
because the server runs there. The password protects the published host port
from other host processes, not the agent from itself.

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
    env: [WORKSPACE_DIR, OPENCODE_SERVER_PASSWORD, DEEPSEEK_API_KEY, OPENCODE_API_KEY,
          HTTP_PROXY, HTTPS_PROXY, NO_PROXY]
    background: true
```

Why not `cmd: [serve, ...]` in `sbxenv.yaml`: an attached `sbx env run` is an
exec-style session, and its process is torn down when the session ends, so the
server dies with the terminal. `long-running@1` only stops the sandbox from
auto-stopping; it does not keep a session process alive.

Two details of the hook matter:

- `env:` is required. A `lifecycle@1` hook only sees the variable names it
  declares (plus `PATH`, `HOME`, `PWD`, …). Without the list, the server starts
  without `OPENCODE_SERVER_PASSWORD` (no auth) and without the provider
  credential sentinels.
- `cd "$WORKSPACE_DIR"` sets the server's project root. Startup hooks start in
  the image `WORKDIR`, which is not the mounted workspace.

Start the sandbox without attaching — this boots the sandbox, runs the hook, and
returns your prompt:

```powershell
sbx env run --detach --env-args-file "$env:USERPROFILE\.sbx\zaiware.args"
```

For an in-sandbox TUI while the server runs: `sbx env exec -it -- opencode`.

Port publishing lives in `sbxenv.yaml` (`ports: 4096 -> 4096`, loopback only).
For an existing sandbox, publish it manually:

```powershell
sbx ports zaiware-platform --publish 4096:4096
```

Client side: the server enforces HTTP basic auth with username `opencode` and
the password from `zaiware.args`. Pass those credentials to the client
(`curl -u`, or `http://opencode:<password>@127.0.0.1:4096` where a client accepts
a URL). `OPENCODE_SERVER_PASSWORD` is the current server-side variable name;
`OPENCODE_PASSWORD` is the pre-rename spelling and may not be read by newer
clients.

Do not bind the published port to a non-loopback host IP without TLS: basic
auth would cross the network in cleartext.

The first `sbx env run` asks you to approve a credential binding for each
service (domains `api.deepseek.com` and `opencode.ai`). The file pre-declares
them, so approval is normally silent.

## .NET SDK

`kits/dotnet` bakes a dotnetup-managed SDK into the image at build time. It is
deliberately not a startup hook or an `sbx env exec` install: those repeat the
~200 MB download on every `sbx env rm` / `sbx env run`, and the sandbox is
recreated routinely. A mixin layer is part of the image digest, so every boot
gets the same toolchain with no network round trip.

- Version: `sdkVersion` in `dotnet.yaml` (default `10.0.401`) must match
  `global.json`. Bump both together; the Kit pin is what makes the image
  reproducible.
- Location: the tracked install lives in `/home/agent/.local/share/dotnetup`
  (data directory and SDK). It is installed with `HOME=/home/agent` at build
  time, so the absolute paths in `dotnetup_manifest.json` stay valid after the
  overlay lands on the workload. `dotnetup list`, `update`, and `uninstall`
  work in the sandbox.
- PATH: `/usr/local/bin/dotnet` and `/usr/local/bin/dotnetup` are symlinks, and
  `/usr/local/bin` is already on the sandbox `PATH`. No shell profile or
  startup hook is needed.
- ICU: the opencode workload is a hardened Debian image without `libicu`, and
  the .NET host aborts at startup without it (`Couldn't find a valid ICU
  package`). The Dockerfile copies only the ICU shared libraries from the build
  stage into the overlay; `libssl` and `zlib` come from the workload.
- Egress: the mixin grants NuGet restore (`api.nuget.org`, `*.nuget.org`) and
  the dotnetup update endpoints. Add another feed's host there if the project
  uses one.

The build downloads from `aka.ms` and `builds.dotnet.microsoft.com` on the
host, so it needs host network access; the sandbox itself only needs the
runtime grants above.

Verify after the next sandbox creation:

```powershell
sbx env exec -- dotnet --version
sbx env exec -- dotnetup list
sbx env exec -- bash -lc 'dotnet build Zaiware.Platform.slnx'
```

A Kit change takes effect only when the sandbox is created again. `sbx env rm`
also deletes the sandbox's OpenCode history (`~/.local/share/opencode/opencode.db`),
so copy it out first if it matters.

## Verify

```powershell
# Two distinct placeholders, never the real keys.
sbx env exec -- bash -c 'env | grep -E "DEEPSEEK_API_KEY|OPENCODE_API_KEY"'

# Both must return 200. A 401 means the placeholder reached the API unchanged.
sbx env exec -- bash -c 'curl -s -o /dev/null -w "deepseek %{http_code}\n" \
  -H "Authorization: Bearer $DEEPSEEK_API_KEY" https://api.deepseek.com/v1/models'
sbx env exec -- bash -c 'curl -s -o /dev/null -w "go %{http_code}\n" \
  -H "Authorization: Bearer $OPENCODE_API_KEY" https://opencode.ai/inference/go/openai/v1/models'

# No stored credentials inside the server: placeholders only.
sbx env exec -- bash -c 'env | grep -E "DEEPSEEK_API_KEY|OPENCODE_API_KEY" | cut -d= -f1'

# The API server is listening and enforces the password:
# 200 with credentials, 401 without (or with the wrong password).
$pw = (Get-Content "$env:USERPROFILE\.sbx\zaiware.args") -replace '^serverPassword=', ''
curl.exe -s -o NUL -w "auth %{http_code}`n" -u "opencode:$pw" http://127.0.0.1:4096/global/health
curl.exe -s -o NUL -w "anon %{http_code}`n" http://127.0.0.1:4096/global/health
Remove-Variable pw
```

If `anon` returns 200, the password did not reach the server: `cmd:` is the
launch command, so it must be set in `sbxenv.yaml` (the server reads
`OPENCODE_SERVER_PASSWORD` from the workload environment).

Never run `/connect` in this sandbox — that stores the real key in
`~/.local/share/opencode/opencode.db` as plaintext.

## Models

| Provider id | Example model | Auth |
|---|---|---|
| `deepseek` | `deepseek/deepseek-flash`, `deepseek/deepseek-v4-pro` | `DEEPSEEK_API_KEY` |
| `opencode-go` | `opencode-go/kimi-k3`, `opencode-go/deepseek-v4.1-flash` | `OPENCODE_API_KEY` |
| `opencode` | `opencode/deepseek-v4.1-flash-free` | none (free tier) |

## Lifecycle

- Secrets are host-scoped, so recreating the sandbox keeps them: `sbx env rm && sbx env run`.
- Rotating a key: `sbx secret set deepseek` again. Placeholders are stable, so no restart is needed.
- Session history lives in the sandbox (`~/.local/share/opencode/opencode.db`) and is lost on `sbx rm`. Copy it out if it matters.
- Do not run `sbx reset` — it deletes all stored host secrets.

## Protecting this directory

`sbxenv.yaml` is mounted read-only because it sits at the workspace root.
`sbx/` is not: it is an ordinary part of the workspace, so the agent can edit
the kits that a later `sbx env run` builds and executes. Make it a read-only
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

Trade-off: the whole directory, including this README, is read-only in the
sandbox, so git operations that would modify `sbx/` (checkout to a branch that
touches it, stash, clean) fail there. For a boundary that survives that, move
`kits/` outside the workspace (for example
`%USERPROFILE%\.sbx\zaiware\kits`) and reference the absolute path from
`kits:`, or publish the kits to a registry and pin them by digest.

# Aspire

The Aspire CLI is installed by Aspire's official install script and is on
`PATH` (`aspire --version`). The .NET SDK comes from the `dotnet` Kit at
`/home/agent/.local/share/dotnetup/dotnet`.

## Run an AppHost

From the workspace root:

```bash
# Foreground (Ctrl+C stops it)
aspire run --non-interactive --launch-profile http

# Background, then inspect
aspire start --non-interactive --launch-profile http
aspire ps
aspire describe
aspire logs <resource>
aspire stop
```

Use the `http` launch profile. The image ships no trusted ASP.NET Core HTTPS
development certificate, and the sandbox only publishes loopback ports.
`aspire doctor` reports missing prerequisites.

## Environment

- `DOTNET_ROOT` points at the dotnetup SDK. Without it, the apphost executable
  that DCP starts directly exits with "You must install .NET to run this
  application". Do not clear it.
- `NO_PROXY` / `no_proxy` contain `[::1]`, `.localhost`, and `.dev.localhost`.
  The sandbox resolves `*.localhost` to `::1`, and the proxy does not bypass
  the bracketed IPv6 literal by default. Keep those entries when changing the
  list, or DCP's local resource service and dashboard calls will be sent to
  the proxy.
- The AppHost uses the Docker daemon exposed in the sandbox, so container
  resources show up in `docker ps`. Project resources run as local processes
  and publish their endpoints on sandbox loopback.

## Ports

The AppHost launch profile pins the dashboard to port 15154
(`zaiware-platform.dev.localhost`) and the OTLP/resource endpoints to 19162 and
20029. `sbxenv.yaml` publishes 15154 on host loopback; do not bind it beyond
loopback. If the sandbox predates the mapping, the host adds it with
`sbx ports zaiware-platform --publish 15154:15154` (see `sbx/README.md`).

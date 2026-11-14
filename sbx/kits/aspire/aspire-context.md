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
- `DCP_IP_VERSION_PREFERENCE=IPv4` makes DCP use `127.0.0.1` instead of
  `[::1]`. The proxy does not bypass the bracketed IPv6 literal, so this keeps
  DCP's loopback calls on an address `NO_PROXY` already covers. Do not add
  `[::1]` to `NO_PROXY`: some clients parse it as a regex and can lose their
  proxy.
- `NO_PROXY` / `no_proxy` also contain `.localhost` and `.dev.localhost` for
  the launch-profile hostnames. Keep those entries when changing the list.
- The AppHost uses the Docker daemon exposed in the sandbox, so container
  resources show up in `docker ps`. Project resources run as local processes
  and publish their endpoints on sandbox loopback.

## Ports

The AppHost launch profile pins the dashboard to port 15154
(`zaiware-platform.dev.localhost`) and the OTLP/resource endpoints to 19162 and
20029. `sbxenv.yaml` publishes 15154 on host loopback; do not bind it beyond
loopback. If the sandbox predates the mapping, the host adds it with
`sbx ports zaiware-platform --publish 15154:15154` (see `sbx/README.md`).

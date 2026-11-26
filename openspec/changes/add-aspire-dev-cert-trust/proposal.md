## Why

`aspire doctor` in the sandbox reports three environment warnings that have nothing to do with the AppHost:`no trusted developer certificate`, `multiple HTTPS development certificates`, and `certutil is not available`. Fixing them by hand (`apt-get install libnss3-tools`, `aspire certs clean`, `aspire certs trust`, and exporting `SSL_CERT_DIR`) works but is session-only: a recreated sandbox loses it, and every agent has to rediscover the steps.

The Aspire kit already owns the CLI and the tool-specific environment the AppHost needs. Certificate tooling belongs there too, so every sandbox starts with a clean `aspire doctor` and a usable HTTPS development certificate without sharing a private key between sandboxes.

## What Changes

- Add an `install` network grant for `deb.debian.org` to the Aspire kit so a create-time install hook can fetch Debian packages.
- Add a lifecycle install hook (`user: "0"`) that installs `libnss3-tools` (provides `certutil`) on every create, mirroring the published Playwright kit's apt pattern.
- Add a lifecycle startup hook (`user: agent`) that creates and trusts a per-sandbox ASP.NET Core HTTPS development certificate only when no trusted certificate exists. Nothing is baked into the image.
- Export `SSL_CERT_DIR` from a kit argument so OpenSSL-based clients (the CLI, `dotnet`, doctor's DCP check) discover `~/.aspnet/dev-certs/trust` as well as the system CA directories.
- Update the Aspire kit header, agent context, and sandbox README so they no longer say the image has no trusted certificate or that the doctor warnings are expected. The AppHost keeps the `http` launch profile; publishing remains loopback-only.
- No change to the AppHost, resources, endpoints, or version pins.

## Capabilities

### New Capabilities

- `aspire-sandbox-kit`: how the Aspire kit provisions the sandbox environment for the AppHost: CLI bundle, DCP/IPv4 and transport settings, and the certificate tooling and trust state `aspire doctor` checks.

### Modified Capabilities

<!-- None: the AppHost capability is unchanged. -->

## Impact

- `sbx/kits/aspire/aspire.yaml`: install-phase network grant, `lifecycle@1` capability, `SSL_CERT_DIR` argument.
- `sbx/kits/aspire/aspire-context.md` and `sbx/README.md`: corrected certificate and doctor guidance.
- Sandbox create time: one `apt-get install libnss3-tools` per creation plus a one-time `aspire certs trust` on first boot. Runtime egress is unchanged; only the create-phase policy gains `deb.debian.org`.
- No image digest change (no Dockerfile change); no new persistent volume; certificates are regenerated per sandbox and never pushed to the host.

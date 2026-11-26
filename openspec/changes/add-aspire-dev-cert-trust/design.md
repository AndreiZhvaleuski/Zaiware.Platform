# Design

## Context

See `proposal.md` — Why. Relevant facts:

- `sbx/kits/aspire/aspire.yaml` is a v3 mixin with one `network-policy@1` capability (`runtime:` only today), an `agent-context@1` capability, `provides: aspire@<version>`, and env arguments (`ASPIRE_ALLOW_UNSECURED_TRANSPORT`, `DCP_IP_VERSION_PREFERENCE`, `ASPIRE_CLI_TELEMETRY_OPTOUT`).
- The kit installs the CLI in the image; it has no lifecycle hooks today.
- The published Playwright kit is the local precedent for apt work that cannot be baked: its install hook runs `apt-get` as root at create and its `network-policy@1` declares `install.allow` for the Debian/DHI repositories.
- `aspire doctor` checks: CLI version, DCP connectivity with an exportable trusted certificate, `certutil` availability, and whether `SSL_CERT_DIR` includes `~/.aspnet/dev-certs/trust`.
- `aspire certs trust` creates a certificate only when needed and trusts it per user on Linux. Without `SSL_CERT_DIR`, it exits `PartiallyFailedToTrustTheCertificate` even though it writes the PEM trust file.
- The sandbox has no `sbx` CLI inside it, so a full `sbx env rm`/`create` verification is host work.

## Goals / Non-Goals

**Goals:**

- A freshly created sandbox passes `aspire doctor` with zero warnings.
- The trusted certificate is generated per sandbox; no private key is baked into the kit image or shared across sandboxes.
- The fix survives `sbx env rm` + create without manual steps.

**Non-Goals:**

- Switching the AppHost's default launch profile or publishing the dashboard to the host.
- Adding Firefox/Chromium to the image; `certutil` is installed only so certificate stores can be updated and doctor can verify them.
- Making `SSL_CERT_DIR` a general-purpose TLS configuration for unrelated tools; the default CA directories stay in the value.

## Decisions

### Install `libnss3-tools` at create, not in the overlay

apt packages need the composed base's dpkg database and shared-library closure, which an overlay cannot carry. The published Playwright kit documents and uses exactly this split (browser tree in the overlay, apt libraries in an install hook). Baking `certutil` plus `libnss3`, `libnspr4`, and their transitive libraries would either duplicate or overwrite base libraries.

Alternative rejected: copy `certutil` and its `ldd` closure into `/out`. Fragile across base updates and unnecessary for a tool used only at boot.

### Per-sandbox certificate from a startup hook

The startup hook runs as `agent` on every boot:

```sh
if ! dotnet dev-certs https --check --trust >/dev/null 2>&1; then
  aspire certs trust --non-interactive
fi
```

A trusted certificate is reused; a missing or untrusted one is created once. A certificate generated at image-build time would share one private key across every sandbox, so it is rejected. Running the hook on every boot (not only install) covers a home directory that is recreated or reset independently of the sandbox.

Alternative rejected: `aspire certs clean` on every boot. It would invalidate a valid certificate on each boot and churn the trust stores for no benefit.

### Export `SSL_CERT_DIR` from a kit argument

`aspire certs trust` writes `~/.aspnet/dev-certs/trust`, but OpenSSL only loads it when `SSL_CERT_DIR` includes it. The kit exports:

```
/usr/lib/ssl/certs:/home/agent/.aspnet/dev-certs/trust
```

The value keeps the system directory, so public roots continue to resolve, and it follows the existing kit pattern of tool-specific env arguments. The trust directory is a fixed runtime path (`HOME=/home/agent` is guaranteed by the sandbox), so an installer-supplied value would add no flexibility.

Alternative rejected: setting only `SSL_CERT_DIR` in the startup hook's environment. That would fix the CLI process but not the agent's shells or `dotnet build` runs.

### Keep the `http` launch profile and transport opt-in

The certificate fixes doctor and makes HTTPS usable, but the dashboard is intentionally not published to the host and HTTP keeps host access simple. This change does not alter the AppHost or its launch profile; the existing `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true` stays.

## Risks / Trade-offs

- **Create now depends on `deb.debian.org`** → the grant is install-phase only and limited to that host; if it is unreachable, the install hook fails creation. That is the same failure mode as the Playwright kit's apt hook.
- **First boot pays for certificate generation** → one `aspire certs trust` per sandbox, seconds; repeated boots skip it.
- **A recreated sandbox gets a new certificate** → by design; nothing outside the sandbox pins the key. Any client that cached the old key must reconnect.
- **`SSL_CERT_DIR` is sandbox-wide** → it adds a directory instead of replacing the system one. If another kit or the user overrides it, doctor reports partial trust again.
- **Doctor's duplicate-certificate warning** → a fresh sandbox starts with none; the check-then-trust path does not create a second one. Pre-existing duplicates from older sandboxes are cleared by `aspire certs clean`.
- **Verification requires a host-side recreate** → done in the change's last task; the sandbox cannot run `sbx` itself.

## Migration Plan

1. Apply the kit descriptor, context, and README edits in one commit.
2. Retrieve the commit on the host and run `sbx env plan`, then `sbx env rm` and `sbx env create` (kit changes are create-time).
3. Inside the new sandbox, run `aspire doctor`; expect 6 passed, 0 warnings, 0 failed.
4. Rollback: remove the lifecycle capability, the `install` grant, and the `SSL_CERT_DIR` argument; no image or data migration is needed.

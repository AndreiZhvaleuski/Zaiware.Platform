# Proposal

## Why

The platform's sign-in today is a single-method email-OTP flow behind a glassmorphism prototype design. Before the platform can serve real OIDC clients it needs the full authentication surface users expect — username + password with optional TOTP multi-factor — plus a signed-in account area (profile, avatar) and a login UI that follows modern UI engineering practice. The surrounding package ecosystem has also moved (the Aspire hosting line is now 17.x, this project pins 13.6.x) and stale artifacts linger: the design prototype folder, a pre-upgrade package backup, and an unused EF Core package reference.

## What Changes

- **Redesign the sign-in UI** with a clean, dependency-light presentation: one combined screen offering password sign-in (username or email + password) and the passwordless email-OTP path; a TOTP challenge step for users with two-factor enabled; the old glassmorphism design and its prototype folder are removed.
- **Password sign-in** backed by Duende User Management password authentication (PBKDF2, default policy), resolving the identifier as username first, then email.
- **TOTP second factor**: opt-in enrollment in the account area (QR code + manual key, confirmation code, recovery codes shown exactly once) and a challenge step after password sign-in for enrolled users, with recovery-code fallback.
- **Account area**: profile section (unique username, display name, read-only email, avatar upload with preview) and security section (password set/change, TOTP enable/disable, recovery-code regeneration).
- **Avatar storage on RustFS**: a private `avatars` bucket added to the Aspire AppHost, server-mediated uploads, and presigned-URL redirect serving — the bucket is never public and the profile's `picture` value is a stable application route.
- **UI foundations**: hand-rolled design tokens with dark/light support, per-page CSS isolation, fingerprinted static assets with immutable caching, `no-store` on auth pages, and a carried-over accessibility baseline (aria wiring, focus management, reduced motion, progressive enhancement).
- **Automated end-to-end coverage**: a committed Playwright workspace (sandbox-provided Chromium, headless) exercising the sign-in flows, account edits, avatar serving, theme behavior, and cache headers against a running Aspire AppHost, with deterministic Development seed identities.
- **Package refresh to current lines**: Aspire hosting packages and AppHost SDK to the 17.x line, OpenTelemetry to 1.21.0, add AWSSDK.S3 and QRCoder, remove the unused EF Core package, delete `Directory.Packages.bak`. Duende stays on the latest 8.1.0-preview.7 line.
- **Housekeeping**: archive the completed `add-otp-login` change so its capability spec is synced before this change modifies it (done as the first step of this change).

## Capabilities

### New Capabilities
- `password-totp-login`: Username-or-email + password sign-in, with the combined sign-in screen, TOTP challenge for enrolled users, recovery-code fallback, and bounded half-authenticated state between password success and second-factor completion.
- `user-account`: The authenticated account area — profile management (unique username, display name, avatar) and security management (password set/change, TOTP enrollment/disable, recovery codes).
- `avatar-storage`: Private RustFS-backed avatar object storage with validated server-mediated upload and presigned-URL redirect serving.
- `ui-foundations`: Cross-cutting presentation requirements shared by every page — design tokens with dual theme, CSS isolation, cache correctness, accessibility baseline, progressive enhancement.

### Modified Capabilities
- `otp-login`: The email-OTP passwordless flow keeps its behavior (invite-only, cooldown, attempt limits, session establishment) while its entry point becomes the one-time-code path on the redesigned combined sign-in screen, and its UI follows the new presentation foundations.

## Impact

- `Zaiware.Platform.IdentityServer`: new/rebuilt Razor Pages (combined Login, TwoFactor challenge, restyled EnterOtp, Account area, TotpSetup, RecoveryCodes); profile schema extension (username, picture); RustFS S3 client wiring; avatar upload endpoint and presigned redirect route; QR rendering; Development seed gains username + password; package references change (AWSSDK.S3, QRCoder added; EF package removed).
- `Zaiware.Platform.AppHost`: RustFS resource + `avatars` bucket + reference wiring; Aspire AppHost SDK pin and hosting package versions move to the 17.x line.
- `Directory.Packages.props`: version bumps and additions/removals listed above; `Directory.Packages.bak` deleted.
- `tests/e2e`: new Playwright workspace (sandbox kit: `@playwright/test` 1.61.1, Chromium, headless) with deterministic Development seed identities; specs read OTP codes from Mailpit's HTTP API and compute TOTP codes from a fixed test secret.
- `design/otp-login/`: deleted (superseded).
- OpenSpec: `add-otp-login` archived; `otp-login` spec synced and then modified by this change's delta.
- Database: no schema migration required — profile attributes ride the existing EAV storage tables (schema is registered in-memory at startup).
- Out of scope: forgot-password (documented pattern exists; future change), magic link (no Duende support; built-in-only constraint), real OIDC client applications, production SMTP provider, production object-storage credentials, NIST ooba conformance.

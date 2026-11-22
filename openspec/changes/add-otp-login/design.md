# Design

## Context

See proposal.md — Why. The IdentityServer host (`Zaiware.Platform.IdentityServer`, net10.0) already wires `AddIdentityServer().AddStorage(AddPostgreSql()).AddConfigurationStorage().AddOperationalStorage().AddUserManagement(_ => {})` backed by the Aspire-hosted `IdentityServer` Postgres database, with DataProtection persisted to a mounted volume. It has no Razor Pages, no login UI, no OTP dispatcher, and nothing that requires authentication. The Aspire AppHost (`Zaiware.Platform.AppHost`) models Postgres + the IdentityServer project. A static glassmorphism design prototype exists at `design/otp-login/index.html` (email -> code -> done, six numeric boxes, 30 s resend).

Two hard constraints from the Duende User Management OTP feature (fixed, non-configurable): codes are 8-character Crockford Base32 with a 5-minute lifetime, resends are blocked for 60 seconds after an issue, and a code allows at most 4 verification attempts. These override the prototype's 6-digit/30 s assumptions.

## Goals / Non-Goals

**Goals:**

- Get from current preview.6 packages to IdentityServer 8.1.0-preview.7 / Storage 2.0.0-preview.7 with the deprecated `Duende.UserManagement.IdentityServer8` reference removed, schema migrated.
- A working email-OTP sign-in round trip inside the IdentityServer host, demoable end to end with Mailpit as the mail sink.
- Invite-only: only pre-created users can sign in.
- Login UI faithful to the design prototype's look and behavior, adapted to Duende's fixed OTP parameters.

**Non-Goals:**

- Invitation/registration UI, user admin screens, SCIM, or any way for users to self-provision.
- Real OIDC client applications; the protected home page inside the IdentityServer host is the only relying party.
- Production SMTP provider selection and credential handling.
- NIST SP 800-63B out-of-band conformance (email OTP is treated as a sign-in factor; the docs are explicit that email fails ooba requirements).

## Decisions

### D1: Upgrade to preview.7 by removing, not bumping, the User Management package

IdentityServer 8.1.0-preview.7 ships User Management inside `Duende.IdentityServer` with identical namespaces and type names; the dedicated package is deprecated. The upgrade is: bump `Duende.IdentityServer` and `Duende.Storage.PostgreSql` in `Directory.Packages.props`, delete `Duende.UserManagement.IdentityServer8` from the props file and the csproj, and drop the now-orphaned `Duende.IdentityServer.EntityFramework` entry. `AddUserManagement(...)` keeps working unchanged. Alternative considered: staying on preview.6 — rejected because the package line we build on is deprecated and preview.7 carries eight security fixes.

### D2: Schema migration stays on the existing startup path

Storage 2.0.0-preview.7 adds schema version 3 (foreign keys on links; the migration deletes dangling links). `Program.cs` already calls `IStorageInstanceSchema.MigrateAsync` at startup in Development, which is the documented in-code migration path, and preview.7 migrations self-verify. No migration tooling is introduced in this change; production rule ("migrate before serving traffic") is noted for when a deployment pipeline exists.

### D3: OTP delivery via `UseSmtpOtpDispatcher`, Mailpit in the AppHost

`AddUserManagement(options => options.Authentication(auth => auth.UseSmtpOtpDispatcher(smtp => ...)))` binds an `Smtp` options section. The AppHost adds a Mailpit resource via `CommunityToolkit.Aspire.Hosting.MailPit` and passes it to the identity server with `.WithReference(mailpit)`; the identity server resolves SMTP host/port from the injected connection information, with an `appsettings.json` fallback for running outside Aspire. The dev loop is: enter the seeded user's email -> open Mailpit's web UI -> read the 8-character code -> paste it. Alternatives considered: console `IOtpDispatcher` (zero deps, but never exercises the email path the user explicitly wants), and a production provider (out of scope).

### D4: Invite-only enforced by a custom `IOtpAuthenticator`

Auto-registration is the default: any address that verifies gets a user. To make sign-in invite-only we register a custom `IOtpAuthenticator` (the documented extension point) that checks the address resolves to an existing user before completing authentication and returns failure otherwise. The page-level send step may additionally decline to dispatch for unknown addresses if a suitable by-address lookup exists on the User Management services; that is a hardening refinement, not the enforcement mechanism, and is left to implementation (see Open Questions). Alternative considered: a seeded-allowlist check inside the login page only — rejected because enforcement at the authentication layer can't be bypassed by another caller.

### D5: Razor Pages following the documented two-page flow, token carried via `TempData`

`Pages/Account/Login` (email step), `Pages/Account/EnterOtp` (code step), `Pages/Account/Logout`, plus an `[Authorize]`-protected `Pages/Index`. IdentityServer's `UserInteraction.LoginUrl` / `LogoutUrl` point at them. The OTP token returned by `TrySendOtpAsync` travels through `TempData` into a hidden field on the verify step (the documented pattern), so a resend simply re-runs the send step and replaces the token. `EnterOtp` handles `SendOtpResult.Blocked` by rendering the server's `SendingBlockedUntilUtc` as the countdown, and pattern-matches `OtpAuthenticationResult` for the invite-only failure message. Sign-in uses `IdentityServerConstants.DefaultCookieAuthenticationScheme`; `returnUrl` is validated through `IIdentityServerInteractionService`. Alternative considered: encrypted cookie for the token — no benefit at this scale.

### D6: Design prototype adapted to fixed OTP parameters, not the reverse

The glassmorphism look (mesh backdrop, translucent card, token palette as CSS custom properties) and the prototype's interaction/a11y rules carry over as-is: blur-then-live email validation, paste-aware auto-advance boxes, auto-submit on the completed code, "change email" escape hatch, busy state with locked inputs, `aria-invalid`/`aria-describedby`, polite status announcements, focus moved to the new heading on step change, `prefers-reduced-motion` and `@supports (backdrop-filter)` fallbacks. The deltas are forced by Duende: 8 alphanumeric positions instead of 6 numeric ones, the resend countdown driven by the server's blocked-until value (60 s floor) instead of a client constant, and error copy reflecting the 4-attempt verification cap. No attempt is made to override Duende's code format; a custom authenticator that reshaped codes would forfeit the library's security defaults.

### D7: Development-only seed of one invited user

With auto-registration off, some path must create the first user. In Development, startup seeds one user with a known email address if it does not exist, using the User Management programmatic-registration API. The email is configurable via configuration/environment so demoers can use an inbox they control. A real invitation flow is a future change.

## Risks / Trade-offs

- [Preview-line volatility] `2.0.0-preview.7` / `8.1.0-preview.7` may see further breaking changes before RTM -> pin exact versions in `Directory.Packages.props`; the upgrade task is isolated so future bumps are contained.
- [Schema migration deletes dangling links] The v3 migration removes links whose target no longer exists -> run against the existing dev database and re-verify the login flow; recreating the Aspire database volume is the dev-stage reset switch.
- [Codes may still be emailed to unknown addresses] Enforcement lives at verification, so an unregistered email can receive a code it cannot use -> acceptable for an internal platform; optionally harden at send time if the API supports it (Open Questions).
- [Email OTP is a single factor] Channel compromise equals account compromise, and NIST disallows email as an out-of-band authenticator -> documented as a Non-Goal; TOTP/passkeys remain available as future User Management authenticators.
- [`TempData` cookie carries the OTP token] Token lifetime is bounded by the code's 5-minute expiry and the cookie is session-scoped -> no action; revisit only if the flow grows new entry points.
- [Mailpit exists only in Development] A production deployment with the same code would need a real `Smtp` section -> fail fast on missing SMTP configuration at startup so misconfiguration is loud, not silent.

## Migration Plan

Dev-stage system; the deployable migration is procedural: bump package versions, run the existing startup `MigrateAsync` (or `duende storage migrate`) before the new version serves traffic, verify sign-in. Rollback: revert package pins; note that once schema v3 has run, earlier previews read the database fine but re-running an older binary against a newer schema is unsupported — recreate the dev database volume if a full reset is needed.

## Open Questions

- Is there a User Management API to look up a user by OTP address at send time (to skip dispatching codes to unknown addresses)? To be answered during implementation of D4; enforcement is at verification either way.
- Exact seeding API surface for the Development user (D7) — resolve against the preview.7 package during implementation; the fallback is whatever programmatic registration the User Management services expose.
- Whether `CommunityToolkit.Aspire.Hosting.MailPit`'s injected connection string can bind directly to the `Smtp` options or needs a small mapping shim — decide during the AppHost task.

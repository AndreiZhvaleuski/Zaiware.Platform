# Proposal

## Why

The platform's IdentityServer has storage, user management, and data protection wired up, but no way for a user to actually sign in: there is no login UI, no OTP delivery channel, and nothing that requires authentication. This change delivers the first end-to-end sign-in experience — passwordless email OTP — and moves to the current Duende preview line first, since IdentityServer 8.1.0-preview.7 merges User Management into the core package (deprecating `Duende.UserManagement.IdentityServer8`, which this project still references) and ships eight security fixes.

## What Changes

- **Upgrade Duende packages to the current preview line**: `Duende.IdentityServer` and `Duende.Storage.PostgreSql` to `8.1.0-preview.7` / `2.0.0-preview.7`, which requires a Storage schema migration (schema version 3 adds foreign keys between linked records).
- **Remove the deprecated `Duende.UserManagement.IdentityServer8` package** (its types now ship inside `Duende.IdentityServer` with the same names and namespaces) and prune the unreferenced `Duende.IdentityServer.EntityFramework` entry from `Directory.Packages.props`.
- **Add Mailpit to the Aspire AppHost** via the `CommunityToolkit.Aspire.Hosting.MailPit` hosting integration, so OTP emails are capturable in a local web UI during development.
- **Configure SMTP OTP delivery** in the IdentityServer (`UseSmtpOtpDispatcher` bound from configuration/Aspire reference).
- **Enforce invite-only sign-in** with a custom `IOtpAuthenticator` that rejects unknown email addresses instead of Duende's default auto-registration.
- **Build the OTP login UI** as Razor Pages inside the IdentityServer host, styled after the existing `design/otp-login/index.html` glassmorphism prototype and adjusted to Duende's fixed OTP defaults (8-character alphanumeric code, 60-second resend interval, 4 verification attempts, 5-minute lifetime): email step, code step, resend countdown, sign-out.
- **Add a protected landing page** in the IdentityServer host that triggers the login round-trip, plus a Development-only seed of one invited user so the flow is demoable without an invitation UI.

## Capabilities

### New Capabilities
- `otp-login`: Passwordless email OTP sign-in for the IdentityServer host — sending one-time codes to invited users, verifying them, establishing the sign-in session, and supporting sign-out.

### Modified Capabilities

(none — the project has no existing specs)

## Impact

- `Zaiware.Platform.IdentityServer`: new Razor Pages (login, code entry, logout, protected home), custom `IOtpAuthenticator`, SMTP OTP dispatcher options, IdentityServer `UserInteraction` URL wiring, Development user seed; `AddUserManagement` moves to the core package's extension.
- `Zaiware.Platform.AppHost`: Mailpit resource and reference wiring.
- `Directory.Packages.props`: version bumps, two entries removed, one added (`CommunityToolkit.Aspire.Hosting.MailPit` for the AppHost).
- Database: IdentityServer Postgres database requires a schema migration to storage schema version 3 before the new version serves traffic; the existing Development-startup `MigrateAsync` path covers local runs.
- Out of scope: invitation/registration UI for creating users, real OIDC client applications, production SMTP provider, and NIST-aligned out-of-band conformance (email OTP is treated as a sign-in factor, not an ooba authenticator).

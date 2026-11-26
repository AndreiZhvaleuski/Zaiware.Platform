# Design

## Context

The IdentityServer host (`Zaiware.Platform.IdentityServer`, net10.0) runs Duende IdentityServer 8.1.0-preview.7 + Duende.Storage.PostgreSql 2.0.0-preview.7 with User Management on Postgres, SMTP OTP delivery through the Aspire-hosted Mailpit, an invite-only `IOtpAuthenticator`, Razor Pages for the OTP login flow, and a Development-only user seed. Duende User Management natively provides password authentication (`IPasswordAuthenticator`), TOTP devices and recovery codes (`IUserAuthenticatorsSelfService`, `ITotpAuthenticator`, `PlainBytesTotpKey`, `TotpAuthenticatorUri`), and an EAV profile system (`BuiltInSchemas.UserProfile`, `IUserProfileSelfService`). Two official samples are the reference implementations: the User Management FullSample (password + TOTP 2FA with encrypted-cookie second-factor state) and the PasswordRegistration sample (password set, /Account attribute editing, forgot-password). RustFS ships an Aspire CommunityToolkit hosting integration; its client pattern is plain AWSSDK.S3 with path-style addressing.

## Goals / Non-Goals

**Goals:**

- A redesigned, framework-free sign-in UI with password + optional TOTP and the existing passwordless email-OTP path, following Duende's documented patterns.
- A signed-in account area covering profile (unique username, name, avatar) and security (password, TOTP enrollment, recovery codes).
- Private RustFS avatar storage with presigned redirect serving.
- UI foundations: dual-theme tokens, CSS isolation, correct caching, a11y baseline, progressive enhancement.
- Package refresh to current lines; stale artifacts removed.

**Non-Goals:**

- Magic link sign-in (no Duende support; excluded by the built-in-mechanisms constraint — the OTP token never reaches the dispatch layer, so a cross-device link would require custom token infrastructure).
- Forgot-password (the PasswordRegistration sample demonstrates the pattern; future change).
- Real OIDC client applications, production SMTP, production object-storage credentials, SCIM, admin user management, NIST ooba conformance.

## Decisions

### D1: Razor Pages, not Blazor

Every Duende User Management flow example is Razor Pages; the flows are form-POST shaped (TempData token, returnUrl validation, cookie sign-in, half-auth state); interactive Blazor adds a circuit/WASM payload to the coldest page in the app and fights the cookie form model. Blazor SSR would work but buys nothing for ~8 form pages whose only client needs are auto-advance, countdowns, and a theme toggle — served by one small deferred script. The UI foundations spike re-verifies this against the official samples before page building. Alternative considered: Blazor SSR/Server/WASM — rejected for the reasons above.

### D2: Hand-rolled token CSS with the full foundations kit

Design tokens as CSS custom properties with `color-scheme: light dark` and the `light-dark()` function, an `@supports` fallback to `prefers-color-scheme` blocks, and a ~200-byte blocking inline script for FOUC-free manual theme override (localStorage, system default). Per-page styles in `.cshtml.css` isolation files bundled to `{AssemblyName}.styles.css`; shared tokens/primitives in a small shared sheet (isolation is page-scoped, so the code-input group used by both EnterOtp and TwoFactor lives shared). Caching: `MapStaticAssets()` + `asp-append-version`, plus `Cache-Control: no-store` on auth pages. Alternatives considered: Bootstrap (default look), Tailwind (build step/CLI in a pure .NET repo) — rejected at this page count; a framework buys assembly speed we do not need.

### D3: Password + TOTP follows the FullSample pattern

The combined screen's password handler resolves the identifier via `IUserProfileAdmin.TryGetAsync` on `preferred_username`, falling back to `email`, then calls `IPasswordAuthenticator.TryAuthenticateAsync`. On success with an enrolled TOTP device, a short-lived encrypted half-auth cookie carries the subject and returnUrl to the challenge page, which verifies via `ITotpAuthenticator` (TOTP) or a recovery code, then signs in with the MFA-authenticated claim set. Failures at any step produce one generic error (no identifier/password distinction) to prevent enumeration; the OTP path keeps its existing anti-enumeration posture (email-only entry, enforcement at verification). Alternative considered: revealing which factor failed — rejected for enumeration reasons.

### D4: Magic link dropped

Duende User Management has no magic-link flow. The only built-in composition — a custom `IOtpDispatcher` formatting the OTP email as a link — is same-browser-only (the verification token is returned to the page, never to the dispatcher), which conflicts with the built-in-only constraint and adds a custom email template for a marginal UX gain over the existing code entry. Revisit only if the platform later accepts custom token infrastructure.

### D5: Profile schema extension, no migration

`BuiltInSchemas.UserProfile.Extend(PreferredUserName with IsUnique, Picture)` via `AddInMemoryDataExtensionSchemas`; `preferred_username` becomes a password-login identifier; `picture` stores the stable avatar route. EAV storage needs no schema migration — values ride the existing tables. The Development seed additionally creates a username and password for the invited user.

### D6: RustFS private bucket, presigned redirect serving

AppHost: `builder.AddRustFs("rustfs").AddBucket("avatars")`, referenced by the IdentityServer (connection string carries endpoint, keys, bucket). Client: AWSSDK.S3 with `ForcePathStyle`. Upload: authenticated endpoint validates type/size and PUTs to `avatars/{subjectId}`. Serving: `GET /avatars/{subjectId}` 302-redirects to a freshly presigned GET (short TTL); missing object serves an initials placeholder. The bucket is never public, and `picture` never stores a presigned URL (it would rot) or a direct object URL (it is environment-specific). Alternatives considered: public bucket with direct URLs — rejected by requirement; proxy streaming — rejected in favour of presigned redirects for simplicity and zero server bandwidth.

### D7: QR via QRCoder 1.9.0

`TotpAuthenticatorUri.Generate(issuer, account, key)` rendered server-side as inline SVG by QRCoder (latest stable, published 2026-11-19), plus the Base32 key in groups for manual entry, per the Duende TOTP docs.

### D8: Package refresh, exact pins

Aspire.Hosting.PostgreSQL, CommunityToolkit MailPit, CommunityToolkit RustFs → 17.0.0; the AppHost `Aspire.AppHost.Sdk` pin moves to the matching 17.x version; OpenTelemetry → 1.21.0; add AWSSDK.S3 4.1.2 and QRCoder 1.9.0; remove Npgsql.EntityFrameworkCore.PostgreSQL (no DbContext exists in the solution); delete `Directory.Packages.bak`; Duende stays 8.1.0-preview.7 / 2.0.0-preview.7 (the latest published line). The major Aspire jump is isolated as the first task with a full `aspire run` verification.

### D9: TOTP enrollment and recovery codes per the documented sequence

Enable: `PlainBytesTotpKey.New()` → QR + manual key → confirm with `TryAddTotpDeviceAsync` (pending key held in TempData) → `TryCreateRecoveryCodesAsync` displayed exactly once. Disable: remove all devices. Regenerate: recreate codes (invalidates prior). The challenge page accepts TOTP or a recovery code.

## Risks / Trade-offs

- [Aspire major-line jump 13.x → 17.x] Possible hosting API breaking changes → isolated first task, verified by a full AppHost run; pins kept exact for a quick revert.
- [Preview-line volatility] Duende 8.1.0-preview.7 may see further changes before RTM → exact pins; the previous change already rode this line successfully.
- [Presigned URL TTL vs user experience] Very short TTLs expire mid-view for slow connections → use a TTL comfortably longer than a page view (minutes), and accept that URLs are single-fetch artifacts, not cacheable content.
- [CSS isolation bundle × MapStaticAssets] The `.styles.css` bundle must actually be fingerprinted/cache-busted under the new pipeline → verified in the foundations task; fallback is `asp-append-version` on the bundle link.
- [TOTP depends on DataProtection key persistence] Docs flag this as the top production mistake → already satisfied: keys persist to a mounted volume.
- [Avatar upload abuse surface] Server-mediated upload keeps validation in the application; limits are enforced before any object write.
- [Same-browser constraint noted for future magic links] Recorded so a future revisit starts from an informed position.

## Migration Plan

Dev-stage system. Sequence: package refresh (with full run verification) → foundations → profile schema + seed → sign-in flows → RustFS + avatars → account area → end-to-end matrix. Rollback: revert package pins; page and spec additions are additive. No database migration is required (EAV schema is registered in-memory at startup); recreating the Aspire dev volumes remains the full local reset. If the Aspire 17.x jump breaks the AppHost irreparably, fall back to the latest 13.6.x line and pin RustFs/MailPit packages compatible with it — recorded as the contingency, not the plan.

## Open Questions

- Exact `Aspire.AppHost.Sdk` version on the 17.x line to pin in the AppHost csproj — resolve against NuGet during the package task.
- Whether the CSS isolation `.styles.css` bundle is fingerprinted by `MapStaticAssets` — verified during the foundations task; fallback documented.
- Final avatar limits (proposal: png/jpeg/webp, max 2 MB) — confirm during implementation.
- Exact Duende service/API names for password set/change while signed in (the PasswordRegistration sample is the reference; adjust to its pattern).

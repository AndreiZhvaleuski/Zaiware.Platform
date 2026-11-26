# Design

## Context

The IdentityServer host (`Zaiware.Platform.IdentityServer`, net10.0) runs Duende IdentityServer 8.1.0-preview.7 + Duende.Storage.PostgreSql 2.0.0-preview.7 with User Management on Postgres, SMTP OTP delivery through the Aspire-hosted Mailpit, an invite-only `IOtpAuthenticator`, Razor Pages for the OTP login flow, and a Development-only user seed. Duende User Management natively provides password authentication (`IPasswordAuthenticator`), TOTP devices and recovery codes (`IUserAuthenticatorsSelfService`, `ITotpAuthenticator`, `IRecoveryCodeAuthenticator`, `PlainBytesTotpKey`, `TotpAuthenticatorUri`), and an EAV profile system (`BuiltInSchemas.UserProfile`, `IUserProfileSelfService`). Two official samples are the reference implementations, retrieved with the Duende MCP and transcribed below: the User Management FullSample (password + TOTP 2FA with `SecondFactorStateCookie`, `/Account` login dispatch, `/Manage` setup pages) and the PasswordRegistration sample (password set/change APIs, profile attribute editing). RustFS ships an Aspire CommunityToolkit hosting integration; its client pattern is plain AWSSDK.S3 with path-style addressing. The sandbox provides a Playwright kit (`@playwright/test` 1.61.1, Chromium under `PLAYWRIGHT_BROWSERS_PATH=/opt/ms-playwright`, headless only, zero-config via NODE_PATH) for automated end-to-end verification.

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

### D3: Password + TOTP follows the FullSample pattern exactly

The combined screen's password handler calls `IPasswordAuthenticator.TryAuthenticateAsync(AttributeDefinition, identifier, NonValidatedPassword, ct)` — `OidcStandardAttributes.PreferredUserName` first, falling back to `OidcStandardAttributes.Email` — and treats anything but `PasswordAuthenticationResult.Success` as one generic failure. On success with `TotpDeviceNames.Count > 0`, a `SecondFactorStateCookie` (FullSample pattern: DataProtection-protected JSON in a `__Host-SecondFactorState` cookie — HttpOnly, Secure, SameSite=Strict, 5-minute lifetime — holding the subject id) carries the half-authenticated state; the challenge page reads it, verifies via `ITotpAuthenticator.TryAuthenticateAsync(subjectId, TotpDeviceName.Default, PlainTextTotp, ct)` or `IRecoveryCodeAuthenticator.TryAuthenticateAsync(subjectId, PlainTextRecoveryCode, ct)`, clears the cookie, and signs in with `amr = "mfa"` (`JwtClaimTypes.AuthenticationMethod`). Without the cookie the challenge redirects to the sign-in screen. `__Host-` cookies require HTTPS, so the IdentityServer must be served over HTTPS in Aspire Development (the standard launch profile); otherwise fall back to an unprefixed cookie name locally. Failures at any step produce one generic error to prevent enumeration; the OTP path keeps its existing anti-enumeration posture (email-only entry, enforcement at verification).

Password set/change (resolved from the PasswordRegistration and FullSample sources): `IUserAuthenticatorsSelfService.TryValidatePasswordAsync(subjectId, newPassword, ct)` returns `PasswordCreationResult.Failed.Errors` or `.Success.Password`; `TrySetPasswordAsync(subjectId, password, ct)` sets a first password; `TryChangePasswordAsync(subjectId, NonValidatedPassword current, newPassword, ct)` changes it and verifies the current one.

### D4: Magic link dropped

Duende User Management has no magic-link flow. The only built-in composition — a custom `IOtpDispatcher` formatting the OTP email as a link — is same-browser-only (the verification token is returned to the page, never to the dispatcher), which conflicts with the built-in-only constraint and adds a custom email template for a marginal UX gain over the existing code entry. Revisit only if the platform later accepts custom token infrastructure.

### D5: Profile schema extension, no migration

`BuiltInSchemas.UserProfile.Extend(PreferredUserName with IsUnique, Picture)` via `AddInMemoryDataExtensionSchemas`; `preferred_username` becomes a password-login identifier; `picture` stores the stable avatar route. EAV storage needs no schema migration — values ride the existing tables. The Development seed additionally creates a username and password for the invited user.

### D6: RustFS private bucket, presigned redirect serving

AppHost: `builder.AddRustFs("rustfs").AddBucket("avatars")`, referenced by the IdentityServer (connection string carries endpoint, keys, bucket). Client: AWSSDK.S3 with `ForcePathStyle`. Upload: authenticated endpoint validates type/size and PUTs to `avatars/{subjectId}`. Serving: `GET /avatars/{subjectId}` 302-redirects to a freshly presigned GET (short TTL); missing object serves an initials placeholder. The bucket is never public, and `picture` never stores a presigned URL (it would rot) or a direct object URL (it is environment-specific). Alternatives considered: public bucket with direct URLs — rejected by requirement; proxy streaming — rejected in favour of presigned redirects for simplicity and zero server bandwidth.

### D7: QR via QRCoder 1.9.0

`TotpAuthenticatorUri.Generate(issuer, account, key)` rendered server-side as inline SVG by QRCoder (latest stable, published 2026-11-19), plus the Base32 key in groups for manual entry, per the Duende TOTP docs.

### D8: Package refresh, exact pins

Aspire.Hosting.PostgreSQL, CommunityToolkit MailPit, CommunityToolkit RustFs → 17.0.0; the AppHost `Aspire.AppHost.Sdk` pin moves to **17.0.0** (published 2026-11-18); OpenTelemetry → 1.21.0; add AWSSDK.S3 4.1.2 and QRCoder 1.9.0; remove Npgsql.EntityFrameworkCore.PostgreSQL (no DbContext exists in the solution); delete `Directory.Packages.bak`; Duende stays 8.1.0-preview.7 / 2.0.0-preview.7 (the latest published line). The major Aspire jump is isolated as the first task with a full `aspire run` verification.

### D9: TOTP enrollment and recovery codes per the documented sequence

Enable: `PlainBytesTotpKey.New()` → QR + manual key → confirm with `TryAddTotpDeviceAsync` (pending key held in TempData) → `TryCreateRecoveryCodesAsync` displayed exactly once. Disable: remove all devices. Regenerate: recreate codes (invalidates prior). The challenge page accepts TOTP or a recovery code.

### D10: Playwright end-to-end suite

Automated E2E coverage uses the sandbox Playwright kit: `@playwright/test` 1.61.1 with Chromium under `PLAYWRIGHT_BROWSERS_PATH=/opt/ms-playwright`, headless only, zero-config resolution via NODE_PATH. A committed `tests/e2e` workspace (minimal `package.json`, `playwright.config.js`, specs) runs with `npx playwright test` against a running `aspire run` stack. Tests drive real flows in the browser; OTP codes are read from Mailpit's HTTP API; TOTP codes are computed in-test from a fixed Base32 seed. Determinism: the Development seed ensures two E2E identities idempotently — a password-only user and a user with a fixed, known TOTP secret — so challenge tests never depend on random enrollment state; specs that mutate profile state restore it at the end. Alternatives considered: a .NET `Microsoft.Playwright` test project — rejected because the sandbox kit provides the Node build of the browsers, and a Node E2E workspace avoids a second browser download inside the sandbox.

### D11: Duende MCP is the reference source for flow code

Implementation must consult the Duende MCP samples (FullSample, PasswordRegistration) before writing flow code, per the server's instruction to prefer official samples over training data. The patterns already extracted: password auth and change APIs (D3), the second-factor state cookie, `/Account`–style profile updates via `AttributeValueCollection` + `TryUpdateAsync` + re-sign-in, TOTP setup with QRCoder, and `SeedData`-style Development seeding. Where the samples and this design differ (email login vs username-then-email, read-only email), the design wins; everything else is transcribed rather than reinvented.

## Risks / Trade-offs

- [Aspire major-line jump 13.x → 17.x] Possible hosting API breaking changes → isolated first task, verified by a full AppHost run; pins kept exact for a quick revert.
- [Preview-line volatility] Duende 8.1.0-preview.7 may see further changes before RTM → exact pins; the previous change already rode this line successfully.
- [Presigned URL TTL vs user experience] Very short TTLs expire mid-view for slow connections → use a TTL comfortably longer than a page view (minutes), and accept that URLs are single-fetch artifacts, not cacheable content.
- [CSS isolation bundle × MapStaticAssets] The `.styles.css` bundle must actually be fingerprinted/cache-busted under the new pipeline → verified in the foundations task; fallback is `asp-append-version` on the bundle link.
- [TOTP depends on DataProtection key persistence] Docs flag this as the top production mistake → already satisfied: keys persist to a mounted volume.
- [Avatar upload abuse surface] Server-mediated upload keeps validation in the application; limits are enforced before any object write.
- [Same-browser constraint noted for future magic links] Recorded so a future revisit starts from an informed position.
- [`__Host-` cookies require HTTPS] The second-factor and OTP cookies use the `__Host-` prefix (Secure) → serve the IdentityServer over HTTPS in Aspire Development (standard launch profile); otherwise use unprefixed cookie names for local HTTP runs.
- [E2E state leakage across runs] Specs mutate usernames, passwords, and TOTP state → deterministic E2E seeds (fixed TOTP key, idempotent seeding) plus per-spec restore steps; recreate the dev database volume for a clean baseline when needed.
- [Playwright is headless-only in the sandbox] No `--headed`, `--ui`, or `codegen` → debug with traces and screenshots (`--trace on`, `--screenshot`), which the kit supports.

## Migration Plan

Dev-stage system. Sequence: package refresh (with full run verification) → foundations → profile schema + seed → sign-in flows → RustFS + avatars → account area → end-to-end matrix. Rollback: revert package pins; page and spec additions are additive. No database migration is required (EAV schema is registered in-memory at startup); recreating the Aspire dev volumes remains the full local reset. If the Aspire 17.x jump breaks the AppHost irreparably, fall back to the latest 13.6.x line and pin RustFs/MailPit packages compatible with it — recorded as the contingency, not the plan.

## Open Questions

- Whether the CSS isolation `.styles.css` bundle is fingerprinted by `MapStaticAssets` — verified during the foundations task; fallback documented.
- Final avatar limits (proposal: png/jpeg/webp, max 2 MB) — confirm during implementation.
- Which Mailpit endpoint form (Aspire-injected endpoint vs a pinned host port) the Playwright suite consumes — resolve when wiring the E2E workspace.

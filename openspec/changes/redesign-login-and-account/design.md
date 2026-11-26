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

The combined screen's password handler calls `IPasswordAuthenticator.TryAuthenticateAsync(AttributeDefinition, identifier, NonValidatedPassword, ct)` — `OidcStandardAttributes.PreferredUserName` first, falling back to `OidcStandardAttributes.Email` — and treats anything but `PasswordAuthenticationResult.Success` as one generic failure. On success with `TotpDeviceNames.Count > 0`, a `SecondFactorStateCookie` (FullSample pattern: DataProtection-protected JSON in a `__Host-SecondFactorState` cookie — HttpOnly, Secure, SameSite=Strict, 5-minute lifetime — holding the subject id) carries the half-authenticated state; the challenge page reads it, verifies via `ITotpAuthenticator.TryAuthenticateAsync(subjectId, TotpDeviceName.Default, PlainTextTotp, ct)` or `IRecoveryCodeAuthenticator.TryAuthenticateAsync(subjectId, PlainTextRecoveryCode, ct)`, clears the cookie, and signs in with `amr = "mfa"` (`JwtClaimTypes.AuthenticationMethod`). Without the cookie the challenge redirects to the sign-in screen. `__Host-` cookies require HTTPS, so the IdentityServer is served over HTTPS in Aspire Development (`SelfHost` launch profile). There is deliberately **no plain-HTTP fallback cookie name**: the cookie is always `__Host-SecondFactorState`, Secure, HttpOnly, SameSite=Strict, Path=/, and a request over HTTP simply cannot store/send it, so the challenge bounces to sign-in. HTTPS is a hard requirement for TOTP sign-in. Failures at any step produce one generic error to prevent enumeration; the OTP path keeps its existing anti-enumeration posture (email-only entry, enforcement at verification).

Password set/change (resolved from the PasswordRegistration and FullSample sources): `IUserAuthenticatorsSelfService.TryValidatePasswordAsync(subjectId, newPassword, ct)` returns `PasswordCreationResult.Failed.Errors` or `.Success.Password`; `TrySetPasswordAsync(subjectId, password, ct)` sets a first password; `TryChangePasswordAsync(subjectId, NonValidatedPassword current, newPassword, ct)` changes it and verifies the current one.

### D4: Magic link dropped

Duende User Management has no magic-link flow. The only built-in composition — a custom `IOtpDispatcher` formatting the OTP email as a link — is same-browser-only (the verification token is returned to the page, never to the dispatcher), which conflicts with the built-in-only constraint and adds a custom email template for a marginal UX gain over the existing code entry. Revisit only if the platform later accepts custom token infrastructure.

### D5: Profile schema extension, no migration

`BuiltInSchemas.UserProfile.Extend(PreferredUserName with IsUnique, Picture)` via `AddInMemoryDataExtensionSchemas`; `preferred_username` becomes a password-login identifier; `picture` stores the stable avatar route. EAV storage needs no schema migration — values ride the existing tables. The Development seed additionally creates a username and password for the invited user.

### D6: RustFS private bucket, presigned redirect serving

AppHost: `builder.AddRustFs("rustfs").WithHostPort(9000).AddBucket("avatars")`, referenced by the IdentityServer (connection string carries endpoint, keys, bucket). Client: AWSSDK.S3 with `ForcePathStyle`. Upload: authenticated endpoint validates type/size and PUTs to `avatars/{subjectId}`. Serving: `GET /avatars/{subjectId}` 302-redirects to a freshly presigned GET (5-minute TTL); missing object serves an initials placeholder. Presigning uses a second S3 client when `Avatars:PublicEndpoint`/`AVATARS__PUBLICENDPOINT` is configured, because the signature is bound to the host the browser can reach; PUT/HEAD stay on the internal endpoint. The bucket is never public, and `picture` never stores a presigned URL (it would rot) or a direct object URL (it is environment-specific). Alternatives considered: public bucket with direct URLs — rejected by requirement; proxy streaming — rejected in favour of presigned redirects for simplicity and zero server bandwidth.

### D7: QR via QRCoder 1.9.0

`TotpAuthenticatorUri.Generate(issuer, account, key)` rendered server-side as a PNG data URI by QRCoder's `PngByteQRCode` (latest stable, published 2026-11-19), plus the Base32 key in groups for manual entry, per the Duende TOTP docs. The earlier design said SVG; the implementation uses PNG because `PngByteQRCode` is the stable, dependency-light API in 1.9.0 and the QR is only an `<img>` source.

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
- [`__Host-` cookies require HTTPS] The second-factor cookie always uses the `__Host-` prefix (Secure, Path=/). Plain HTTP is unsupported for second-factor sign-in; the IdentityServer runs HTTPS in Aspire Development via the explicit `SelfHost` profile, and there is no fallback name (user decision after review).
- [E2E state leakage across runs] Specs mutate usernames, passwords, and TOTP state → deterministic E2E seeds (fixed TOTP key, idempotent seeding) plus per-spec restore steps; recreate the dev database volume for a clean baseline when needed.
- [Playwright is headless-only in the sandbox] No `--headed`, `--ui`, or `codegen` → debug with traces and screenshots (`--trace on`, `--screenshot`), which the kit supports.

## Migration Plan

Dev-stage system. Sequence: package refresh (with full run verification) → foundations → profile schema + seed → sign-in flows → RustFS + avatars → account area → end-to-end matrix. Rollback: revert package pins; page and spec additions are additive. No database migration is required (EAV schema is registered in-memory at startup); recreating the Aspire dev volumes remains the full local reset. If the Aspire 17.x jump breaks the AppHost irreparably, fall back to the latest 13.6.x line and pin RustFs/MailPit packages compatible with it — recorded as the contingency, not the plan.

## Resolved Questions

- The CSS isolation `.styles.css` bundle is fingerprinted by `MapStaticAssets`; the page emits the fingerprinted URL and the response is served with immutable caching. `asp-append-version` also covers the shared token and primitive sheets.
- Avatar limits are PNG, JPEG, and WebP, 2 MB, with a magic-byte check in addition to the content-type allow-list.
- The Playwright suite consumes Mailpit over HTTP at a pinned port (`8025`, with SMTP on `1025`); `MAILPIT_BASE_URL` defaults to `http://localhost:8025` and can be overridden.

## Implementation Notes

### 2.1 Foundations spike adjustments

The FullSample (`229fefbd-f371-4774-bc36-f15aa7cd1494`) and PasswordRegistration (`b1741efe-2ce2-487d-80a4-d84844d34337`) sources were reviewed against this design. Adjustments:

- `IPasswordAuthenticator.TryAuthenticateAsync` takes an `AttributeCode` (an `AttributeDefinition` converts implicitly), not an `AttributeDefinition` parameter: `TryAuthenticateAsync(OidcStandardAttributes.PreferredUserName, identifier, NonValidatedPassword.Create(password), ct)`. The design's signature still maps, but the code uses the implicit conversion.
- Duende User Management 8.1's default `PasswordOptions.MinLength` is **15**, not 8, and no character-class minimums are required. The seed and E2E passwords therefore use 15+ character passphrases (for example `E2e-Password-2026!`).
- The sample's `SecondFactorStateCookie` serializes a subject id with DataProtection and writes a `__Host-SecondFactorState` cookie with `Secure`, `HttpOnly`, `SameSite=Strict`, `IsEssential`, and a five-minute `Expires`. Our implementation follows it, but reads with `UserSubjectId.TryCreate` instead of `UserSubjectId.Create` so a malformed payload cannot throw, and sets `Path = "/"` explicitly (required by the `__Host-` prefix).
- The sample has a separate `LoginWithRecoveryCode` page. This design combines TOTP and recovery-code entry on one challenge page; both `ITotpAuthenticator` and `IRecoveryCodeAuthenticator` are available for that composition.
- Password seeding: the FullSample seeds a password through `IUserImporter` and `IPasswordHashAlgorithm`, while PasswordRegistration shows `TryValidatePasswordAsync` + `TrySetPasswordAsync`. The latter is the documented backend-capable self-service API (the docs explicitly allow seed scripts to use it) and avoids hashing by hand, so the Development seed uses `TryValidatePasswordAsync` → `TrySetPasswordAsync`.
- The sample's profile schema administration (`IUserProfileSchemaAdmin`) predates the documented in-memory extension API. This design uses `BuiltInSchemas.UserProfile.Extend(preferredUsername, picture)` + `AddInMemoryDataExtensionSchemas`, which the current docs recommend for code-owned schemas.
- The sample signs in with `IdentityServerUser` and `amr` additional claims. All three flows (password, password+TOTP, and OTP) now use the shared `UserSessionSignIn`, which adds `email`, `name`, `preferred_username`, and the matching `amr` (`pwd`, `mfa`, or `otp`).
- The sample's TOTP setup uses `PlainBytesTotpKey.New()`, `TotpAuthenticatorUri.Generate`, QRCoder `PngByteQRCode`, `TryAddTotpDeviceAsync`, and `TryCreateRecoveryCodesAsync`; our page transcribes those calls and adds an inline PNG QR data URI plus a manual key.
- The sample's `ManageProfile` replaces the whole attribute set from a fresh collection. Our account update starts from the existing values (`new AttributeValueCollection(schema, profile.Attributes.Values)`) so unrelated attributes survive partial updates.
- Recovery-code generation returns `null` when two-factor is not enabled; the security page treats that as "no codes yet" rather than an error.


## Review Triage (post-implementation)

A static review plus dynamic Playwright verification found gaps after the first implementation pass. The fixes below are in the committed change:

- **Auth input null-safety**: `LoginModel.InputModel` fields are nullable; both handlers normalize before use. Empty/whitespace password and OTP submissions render validation, never 500. `TotpSetup`/`TwoFactor` normalize nullable codes. Unit tests: `LoginModelTests`, `TotpCodeTests`.
- **Fail-closed MFA**: if `IUserAuthenticatorsSelfService.TryGetAsync` returns `null`, password sign-in logs an error, shows a generic temporary message, and issues no session. Unit test `LoginModelTests.Missing_authenticator_state_fails_closed_and_does_not_sign_in`.
- **Half-auth expiry**: `SecondFactorStateCookie` stores `IssuedAtUtc` inside the protected JSON and rejects payloads older than five minutes through an injected `TimeProvider`. Tests cover round-trip, stale, tampered, malformed, and clear.
- **Session cookie**: `UserSessionSignIn` issues a non-persistent browser-session cookie (`IsPersistent=false`, no `ExpiresUtc`) for password, TOTP, and OTP, matching the old OTP behavior. Unit test asserts the properties and `amr=mfa`.
- **OTP anti-enumeration**: the OTP request no longer pre-checks the profile. Every syntactically valid address dispatches a code and advances to code entry; `InviteOnlyOtpAuthenticator` remains the enforcement point and never creates a user. E2E covers malformed, unknown-but-valid, resend invalidation, returnUrl, and malicious returnUrl.
- **Return URLs**: shared `SafeReturnUrl.Normalize/Redirect` uses `LocalRedirectResult` only for local URLs and `RedirectResult` for external URLs already validated by `IIdentityServerInteractionService`. Used by Login GET/password, TwoFactor GET/TOTP/recovery, and EnterOtp. Unit + E2E coverage.
- **Account error renders**: avatar/password/disable/regenerate paths call `Load(profile)` so username and display name stay populated; only `OnPostProfileAsync` preserves bound values. E2E asserts the fields after an oversized avatar and a weak password.
- **Upload limit**: the Account model carries `[RequestSizeLimit]` and `[RequestFormLimits]` at 2 MB + 64 KB. E2E posts a 3 MB body and accepts 400/413, then verifies the stored avatar is unchanged.
- **Magic bytes**: extracted to `AvatarSignature` with `ReadAtLeastAsync`; a one-byte-per-read stream cannot false-reject a valid PNG. Unit tests cover PNG/JPEG/WebP, short, wrong, spoofed, and short-read streams.
- **Security headers/CSP**: `SecurityHeadersMiddleware` adds nosniff, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, and `default-src 'self'; base-uri 'self'; frame-ancestors 'none'; object-src 'none'; script-src 'self' 'nonce-…'; style-src 'self'; img-src 'self' data:; form-action 'self'`. The nonce goes on the inline theme script; the theme toggle still passes E2E.
- **Caching**: `AuthPageNoStoreMiddleware` applies no-store via `OnStarting` to `/` and `/Account*`, including 302s; `ImmutableStaticAssetCachingMiddleware` keeps fingerprinted assets immutable. E2E checks the signed-in `/`, the anonymous `/Account` 302, and immutable JS.
- **Tag helpers**: `Pages/_ViewImports.cshtml` registers the standard tag helpers. The rendered brand link has `href="/"`, `asp-append-version` is processed (no literal attribute), CSS/JS remain fingerprinted, and `ui.js` carries the version attribute. E2E checks rendered `script[src]` versioning and immutable caching.
- **Interfaces/seams**: `IUserSessionSignIn`, `ISecondFactorStateCookie`, `IAvatarStorage`, and `IFlashMessage` are registered in DI; implementations stay sealed. Page models depend on the interfaces.
- **Flash messages**: page-scoped `flash:success:{pagePath}` TempData keys replace generic `SuccessMessage` for Account, TotpSetup, and RecoveryCodes; only the destination page consumes/removes the value. The RecoveryCodes text is neutral ("Recovery codes saved.").
- **Middleware split**: the inline Program middleware is replaced by `AuthPageNoStoreMiddleware`, `ImmutableStaticAssetCachingMiddleware`, and the security-header middleware, with `UseAuthPageNoStore()`, `UseImmutableStaticAssetCaching()`, and `UseSecurityHeaders()` extensions.
- **TOTP normalization**: `TotpCode.Normalize` strips spaces/hyphens/non-digits and requires exactly six digits on the server; `ui.js` does the same for UX (`data-totp-input`). The setup pattern accepts separators; validation is server-authoritative.
- **Aspire**: `AddDotnetProject("identity-server", …, options => options.LaunchProfileName = "SelfHost")` replaces obsolete `AddProject<T>`; `Aspire.Hosting.Dotnet` 17.0.0 is pinned. The expected `ASPIRE012` SDK-obsolete warning is accepted and documented. `AVATARS__PUBLICENDPOINT` is configurable with the local default `http://localhost:9000`; RustFS host port 9000 keeps that default stable.
- **Seed**: E2E identities carry `ResetPassword: true`; general users keep a changed password. A configured TOTP secret is reconciled by verifying the current code and rebuilding the default device when it no longer matches.
- **UI/a11y**: code groups wrap to 4×2 (8-char) and 3×2 (6-char) under 26rem so cells are ≥44px at 320px; Account/TotpSetup/RecoveryCodes headings are focus targets; password/security/TOTP/recovery inputs expose `aria-invalid` and `aria-describedby`; `role=alert` no longer also carries `aria-live=polite`; the primitives sheet has the small reset (form controls, images, fieldsets, nav lists).
- **Tests**: `Zaiware.Platform.IdentityServer.Tests` uses xUnit v3 3.2.2 with Microsoft.Testing.Platform (`OutputType=Exe`, `UseMicrosoftTestingPlatformRunner`, `TestingPlatformDotnetTestSupport`), not MSTest. `global.json` remains authoritative for `dotnet test`.

### Policy and documentation answers

- **Built-in schema (U6)**: the profile uses `BuiltInSchemas.UserProfile.Extend(...)` rather than a custom schema because email/name are built-in and only `preferred_username` (unique) and `picture` are platform additions. EAV storage needs no migration; `AddInMemoryDataExtensionSchemas` registers the code-owned extension.
- **`color-scheme` / `light-dark()` (U7/U8)**: `color-scheme: light dark` lets native controls follow the theme. Tokens use `light-dark()` where supported, with `@supports` fallbacks to `prefers-color-scheme` blocks for older engines; the inline theme script only overrides `data-theme` when the user chose a theme, so system preference remains the default.
- **Entity Framework (U10)**: the EF Core package was removed because the solution has no `DbContext` and Duende storage uses Npgsql/its own repository; keeping it added restore/build weight with no code path.
- **Encrypted JSON (U11)**: `SecondFactorStateCookie` protects a small JSON payload (`SubjectId`, `IssuedAtUtc`) with Data Protection so the browser cannot read or forge the half-auth subject, and the server can enforce expiry independently of the cookie header.
- **`__Host-` (U12/M11)**: the production cookie is always `__Host-SecondFactorState` with Secure/HttpOnly/SameSite=Strict/Path=/. Per the post-review user decision there is no HTTP fallback name; plain HTTP is unsupported for second-factor sign-in (browsers reject the cookie and the challenge bounces to sign-in). The AppHost `SelfHost` profile is HTTPS.
- **`amr` (U15)**: `UserSessionSignIn` sets `amr` to `pwd`, `mfa`, or `otp` so downstream clients can distinguish the factor; the property is exercised by `UserSessionSignInTests`.
- **OTP vs TOTP (H3)**: OTP remains an alternative sign-in factor and intentionally bypasses TOTP; the Account security section states this explicitly. This preserves the OTP spec's behavior rather than silently changing the policy.
- **Avatar visibility (L11)**: avatars are readable by any authenticated user via `/avatars/{subjectId}`; the route is not self-only. This is documented rather than enforced because profile pictures are shared identity data; the bucket remains private and the route still requires authentication.
- **TOTP disable (L10)**: disabling removes every TOTP device; stored recovery codes are left in place but are unusable until TOTP is re-enabled, and regeneration is only offered while TOTP is enabled. This avoids destructive recovery-code deletion through an API that has no dedicated remove operation.
- **Placeholder theme (L6)**: the initials SVG uses a fixed brand blue/white pair for stable contrast in both themes; it is a brand mark, not a theme-token surface.
- **Minification (U3)**: no runtime or build minifier is added. `MapStaticAssets` fingerprints and compresses assets; adding a JS/CSS build step to this small Razor Pages app would add tooling and a second source of truth for no meaningful payload win.
- **Mailpit (L4)**: ports 8025/1025 are fixed Development conventions; this is documented as a dev limitation and can be overridden through `Smtp`/`MAILPIT_BASE_URL` for a different topology.
- **Aspire warning (L5/V2)**: `ASPIRE012` remains because the AppHost keeps `Sdk="Aspire.AppHost.Sdk/17.0.0"` for a staged migration; `AddProject<T>` is gone in favour of `AddDotnetProject`, so the actionable CS0618 warning is resolved and only the documented SDK-obsolete warning remains.

## Verification Results

### Environment constraint (V4)

The sandbox network policy blocks Docker registries. An explicit `aspire start --non-interactive --isolated --launch-profile http --apphost Zaiware.Platform.AppHost/Zaiware.Platform.AppHost.csproj` attempt (Aspire CLI 13.6.4, AppHost SDK 17.0.0) started the AppHost, but Postgres, Mailpit, RustFS, and the bucket all reported `FailedToStart`. The logs show `403 Forbidden` from `registry-1.docker.io` for `postgres:18.6` and `axllent/mailpit:v1.31.2`; `mcr.microsoft.com` is blocked the same way. This remains the reason tasks 1.1, 5.1, and 7.1 are unchecked. The Aspire kit now grants the registry domains at runtime (`registry-1.docker.io`, `auth.docker.io`, `production.cloudflare.docker.com`, `*.r2.cloudflarestorage.com`, `mcr.microsoft.com`, `docker.io`) and defaults to 17.0.0, but that policy applies to the next sandbox recreate and could not be verified here.

The IdentityServer was verified natively over HTTPS with the Development certificate, PostgreSQL 17.12, Mailpit 1.34.0 (SMTP 1025, HTTP 8025), and RustFS 1.0.7 (S3 9000). Before the recorded runs the database was dropped and recreated and `/tmp/dpkeys` was cleared; the `avatars` bucket was kept. `dotnet run --launch-profile SelfHost` served `https://localhost:5001`.

### Automated checks

- `dotnet build Zaiware.Platform.slnx`: succeeded with one accepted warning, `ASPIRE012` (documented above). No CS0618/AddProject warning remains.
- `dotnet test Zaiware.Platform.slnx`: **71 passed, 0 failed** through Microsoft.Testing.Platform (xUnit.net v3 MTP runner). The count includes avatar placeholder/signature/options, cookie round-trip/stale/tamper/clear, flash scoping, profile schema, TOTP normalization, Login empty/null/fail-closed/anti-enumeration, Account first-password, TotpSetup expired key, SafeReturnUrl, and session-cookie `amr`/persistence.
- Playwright, fresh native stack, one worker, zero retries, `IDENTITY_BASE_URL=https://localhost:5001`, `MAILPIT_BASE_URL=http://localhost:8025`: **51 passed**, and a second consecutive run against the same stack: **51 passed**.
- Rendered `/Account/Login` checks:
  - `<a class="brand" href="/">` is emitted (tag helper processed).
  - No literal `asp-append-version` attribute appears.
  - CSS/JS URLs are fingerprinted (`/css/tokens.<hash>.css`, `/css/primitives.<hash>.css`, `/Zaiware.Platform.IdentityServer.<hash>.styles.css`, `/js/ui.<hash>.js`).
  - Fingerprinted CSS and JS return `200` with `Cache-Control: public, max-age=31536000, immutable`.
- Header checks: `Cache-Control: no-store, no-cache, max-age=0` on `/`, `/Account`, `/Account/Login`, and the anonymous `/Account` 302; `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, and a CSP whose nonce matches the inline theme script.
- Capability coverage:
  - `ui-foundations`: themes/FOUC/persistence, no-store (200 and 302), fingerprinted CSS + JS, immutable headers, security headers/CSP nonce, 320px 44px code targets, no-JS password sign-in, skip link, aria-invalid/describedby, CSS-isolation scope separation, and keyboard flow.
  - `password-totp-login`: username/email sign-in, generic failure, empty/whitespace validation on both forms, signed-in skip, sign-out protection, TOTP challenge with fresh codes and retry, wrong-code rejection, direct-navigation bounce, recovery-code completion/reuse rejection.
  - `otp-login`: Mailpit round trip, cooldown/resend refusal, paste/auto-submit, change-address, malformed address, unknown-but-valid anti-enumeration, resend invalidation, local/malicious returnUrl, four-attempt invalidation.
  - `user-account`: read-only email, username change usable at sign-in, duplicate rejection, display-name persistence, password change/old-password failure, TOTP enroll/abandon/disable/regenerate, recovery-code count/display, and profile fields populated after failed password/avatar actions.
  - `avatar-storage`: placeholder, valid upload with 302 + presigned `Location` and `X-Amz-Expires=300`, subject-key check, 2 MB rejection without replacing bytes, 3 MB body-limit rejection, type/spoof rejection, unauthenticated bounce.
- Seed/restart (U21): a second start created no duplicate profiles; users without a configured secret kept TOTP off. Changing the configured TOTP secret and restarting logged the rotation, and restoring the configured secret rotated the device back; the seed's code verification drives the reconciliation.

### Manual checks

- Reviewed Login and Account in both themes; the earlier "or" divider spacing defect remains fixed. Contrast spot checks on text, muted text, links, buttons, notices, and placeholders met AA (4.71:1 light, 5.20:1 dark).
- Mailpit's UI/API showed the OTP email and code; the fixed dev ports are documented.
- The Aspire dashboard/resource-health check could not be performed: the AppHost and dashboard started, but every container resource failed on the `403 Forbidden` image pull (the exact `aspire start`/`aspire describe`/`aspire logs` evidence is recorded above).

### Implementation deviations

- The Development seed re-applies configured usernames/display names and, for `ResetPassword: true` identities, passwords; the demo identity keeps a manually changed password. TOTP devices are removed for identities without a configured secret and reconciled when the secret changes. Development-only.
- Development password throttling is relaxed (`MaxFailedAttempts` 10, 50 attempts per 30 seconds, 10-second throttle, no escalation) so the password-change matrix does not lock the test identity. Production keeps the Duende defaults.
- Five deterministic E2E identities (`e2e-password`, `e2e-totp`, `e2e-account`, `e2e-security`, `e2e-password-change`) plus the demo user; profile/security tests mutate disposable users rather than the demo identity.
- Mailpit is pinned to host ports 8025/1025; RustFS to 9000; the public avatar endpoint defaults to `http://localhost:9000` and is configurable.
- Presigned URLs are normalized from `https` to `http` when the configured endpoint is plain HTTP; SigV4 signs the host, so the signature remains valid.
- A blocked OTP resend re-stores the pending token so the cooldown response does not end the OTP session.
- Static assets are served with immutable caching only for fingerprinted paths; other development assets keep the framework defaults.

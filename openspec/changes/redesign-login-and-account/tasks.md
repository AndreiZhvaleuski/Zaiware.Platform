# Tasks

## 1. Housekeeping and package refresh

- [ ] 1.1 Bump `Aspire.Hosting.PostgreSQL` and `CommunityToolkit.Aspire.Hosting.MailPit` to 17.0.0 in `Directory.Packages.props`, add `CommunityToolkit.Aspire.Hosting.RustFs` (17.0.0) for the AppHost, and move the AppHost `Sdk="Aspire.AppHost.Sdk/..."` pin to the matching 17.x version. Verify `dotnet build` succeeds and `aspire run` starts Postgres, Mailpit, and the identity server with the existing OTP login flow still working end to end.
- [ ] 1.2 In `Directory.Packages.props`: add `AWSSDK.S3` (4.1.2) and `QRCoder` (1.9.0); bump `OpenTelemetry.*` packages to 1.21.0; remove the `Npgsql.EntityFrameworkCore.PostgreSQL` entry and its PackageReference from the IdentityServer csproj (no DbContext exists). Verify the build succeeds and `dotnet list package` shows no EF Core package in the IdentityServer.
- [ ] 1.3 Delete stale artifacts: `Directory.Packages.bak`, the `design/otp-login/` folder, and the glassmorphism `wwwroot/css/site.css` (replaced by the foundations task). Verify no references to the deleted paths remain (grep for `otp-login` design links and `.bak`).

## 2. UI foundations

- [ ] 2.1 Foundations spike (timeboxed): review the Duende UserManagement FullSample and PasswordRegistration sample implementations (Razor page structure, second-factor encrypted-cookie state, /Account attribute editing) and confirm the CSS approach (hand-rolled tokens; no framework). Record the outcome and any pattern adjustments as a short note in this change. Verify the two samples' flows map onto our pages with no gaps.
- [ ] 2.2 Implement the foundations: `wwwroot/css/tokens.css` (`color-scheme: light dark`, `light-dark()` tokens, `@supports` fallback), `wwwroot/css/primitives.css` (buttons, fields, code-input group, notices), the FOUC-free inline theme script and manual toggle (localStorage, system default) in `_Layout`, skip link, per-page `.cshtml.css` isolation files, `MapStaticAssets()` replacing `UseStaticFiles()`, `asp-append-version` on all stylesheet links, and `Cache-Control: no-store` on all Account and sign-in pages. Verify both themes render without flash, toggling persists across reloads, stylesheet URLs change when content changes, the isolated bundle is fingerprinted or falls back to `asp-append-version`, and sign-in pages send `no-store`.

## 3. Profile schema and Development seed

- [ ] 3.1 Extend the profile schema with a unique `preferred_username` and `picture` via `AddInMemoryDataExtensionSchemas`, and extend `DevelopmentUserSeed` to set a username and a Development password for the seeded user. Verify a fresh Development run seeds the user, password sign-in works with the username, and no schema migration runs (EAV registration only).

## 4. Sign-in flows

- [ ] 4.1 Rebuild the combined sign-in screen (`Login`): identifier + password form with username-then-email resolution and a single generic failure error, plus the one-time-code action leading to the existing send flow, following the ui-foundations requirements (autocomplete tokens, focus and busy states). Verify the seeded user signs in with username and with email, unknown identifier and wrong password show the same generic error, and the one-time-code path still delivers to Mailpit.
- [ ] 4.2 Implement the half-authenticated state (short-lived encrypted cookie carrying subject and returnUrl) and the TwoFactor challenge page: 6-digit TOTP verification with recovery-code fallback, MFA sign-in on success, redirect-to-login when the state is missing or expired. Verify an enrolled user is challenged and completes with both a TOTP code and a recovery code, direct navigation to the challenge bounces to login, and a used recovery code is rejected on reuse.
- [ ] 4.3 Restyle `EnterOtp` under the new foundations (behavior preserved; back-navigation now targets the combined sign-in screen). Verify the existing `otp-login` scenarios still pass: resend cooldown from the server value, attempt limits, change-address escape hatch, and paste-and-autosubmit.

## 5. RustFS and avatars

- [ ] 5.1 Add the RustFS resource with an `avatars` bucket to the AppHost and reference it from the IdentityServer; register an AWSSDK.S3 path-style client from the injected connection string. Verify `aspire run` starts RustFS healthy with the bucket provisioned, and the bucket refuses anonymous object reads.
- [ ] 5.2 Implement the authenticated avatar upload endpoint (type/size validation, PUT to `avatars/{subjectId}`, update the profile's `picture` to the stable route) and the `GET /avatars/{subjectId}` presigned redirect route with an initials placeholder when no object exists. Verify a valid upload is stored and immediately visible via the route's redirect, oversized and wrong-type uploads are rejected with specific errors, and unauthenticated requests to both endpoints bounce to login.

## 6. Account area

- [ ] 6.1 Build the Account page profile section: username (uniqueness errors), display name, read-only email, and avatar upload with preview. Verify a username change makes the new value usable at sign-in, duplicates are rejected, the display name persists, and the avatar preview updates after upload.
- [ ] 6.2 Build the Account security section: password set/change (confirm the current password; policy errors surfaced) and TOTP enrollment per the documented sequence — `PlainBytesTotpKey.New()`, QR as inline SVG via QRCoder plus manual key, confirmation via `TryAddTotpDeviceAsync`, recovery codes displayed exactly once — plus disable (remove devices) and regeneration. Verify the full matrix: enroll → sign out → password sign-in presents the challenge → complete; abandon enrollment before confirming → no challenge; disable → no challenge; regenerate → old codes invalid.

## 7. End-to-end verification

- [ ] 7.1 Run the complete matrix through the Aspire AppHost with Mailpit and record the results in the change notes: seeded user signs in by username and by email; wrong credentials give one generic error; TOTP challenge for enrolled users (TOTP + recovery fallback, reuse rejected); unenrolled users sign straight in; email-OTP passwordless still works end to end with cooldown and attempt limits; account profile edits (username uniqueness, name, avatar) persist; avatar serves via presigned redirect with placeholder fallback; theme toggle persists; keyboard-only and JavaScript-disabled sign-ins complete; auth pages send `no-store` and assets are fingerprinted.

## Workflow follow-up

- Archive this change after the implementation is reviewed and the end-to-end matrix passes.
- Verify the archived specs (`otp-login`, `password-totp-login`, `user-account`, `avatar-storage`, `ui-foundations`) appear under `openspec/specs/`.

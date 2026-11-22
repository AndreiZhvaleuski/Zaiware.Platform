# Tasks

## 1. Package upgrade and schema migration

- [x] 1.1 In `Directory.Packages.props`, bump `Duende.IdentityServer` to `8.1.0-preview.7` and `Duende.Storage.PostgreSql` to `2.0.0-preview.7`, and remove the `Duende.UserManagement.IdentityServer8` and `Duende.IdentityServer.EntityFramework` entries; remove the `Duende.UserManagement.IdentityServer8` PackageReference from `Zaiware.Platform.IdentityServer.csproj`. Verify with `dotnet build` succeeding and `dotnet list package` showing no deprecated Duende packages.
- [x] 1.2 Run the app in Development and confirm the startup `IStorageInstanceSchema.MigrateAsync` applies Storage schema version 3 against the IdentityServer Postgres database without errors (check logs; reset the Aspire database volume and retry if the migration hits stale dangling links).

## 2. Mailpit and SMTP OTP delivery

- [x] 2.1 Add `CommunityToolkit.Aspire.Hosting.MailPit` to the AppHost and wire `builder.AddMailPit("mailpit")` into `AppHost.cs` with `.WithReference(mailpit)` and `.WaitFor(mailpit)` on the identity server project. Verify `aspire run` (or `dotnet run` on the AppHost) starts the Mailpit container and the identity server, with Mailpit's web UI reachable on its dashboard endpoint.
- [x] 2.2 Configure `UseSmtpOtpDispatcher` inside `AddUserManagement` options, resolving SMTP host/port from the Aspire-injected connection information with an `appsettings.json` fallback, and fail fast at startup when SMTP configuration is missing. Verify the app refuses to start with a clear error when the `Smtp` section and Aspire reference are both absent, and starts cleanly when either is present.

## 3. Invite-only enforcement and Development user seed

- [x] 3.1 Implement the custom invite-only `IOtpAuthenticator` (design D4): verify the OTP address resolves to an existing user before completing authentication, return failure otherwise, and register it so it replaces the default. Verify by unit-level reasoning plus a later end-to-end check (5.1) that an unregistered email cannot sign in and no user row appears for it.
- [x] 3.2 Add a Development-only startup seed that creates one invited user with a configuration-supplied email if it does not already exist (design D7). Verify by starting the app in Development and observing a log line/DB row for the seeded user on first run, and idempotence (no duplicate) on a second run.
- [x] 3.3 Optional hardening, only if the User Management API supports lookup by OTP address (design Open Question): decline to dispatch codes to unknown addresses at the send step. If unsupported, record that finding in the change notes and skip.

## 4. Login pages and UI

- [x] 4.1 Add Razor Pages support (`AddRazorPages`, `UseRouting`/`MapRazorPages`) and point `IdentityServerOptions.UserInteraction.LoginUrl`/`LogoutUrl` at the new pages. Verify an unauthenticated request to a protected route redirects to the login page and a build passes.
- [x] 4.2 Implement `Pages/Account/Login` (email step) calling `IOtpSender.TrySendOtpAsync`, storing the returned token in `TempData`, and handling `SendOtpResult.Blocked` and validation errors per spec. Verify by submitting the seeded user's email in the running app and seeing the code step appear with an email in Mailpit.
- [x] 4.3 Implement `Pages/Account/EnterOtp` (code step): token from `TempData` in a hidden field, resend action that re-sends and swaps the token, countdown rendered from the server's blocked-until value, verification attempt errors, and "change email" back-navigation per spec. Verify resend within cooldown shows the wait, and after cooldown a new code arrives in Mailpit while the old one stops working.
- [x] 4.4 Implement successful sign-in (claims from the result, `SignInAsync` on the IdentityServer cookie scheme, validated `returnUrl` redirect) and `Pages/Account/Logout` (POST). Verify a full email-to-protected-page round trip and that sign-out forces a new login.
- [x] 4.5 Port the glassmorphism design from `design/otp-login/index.html` into the Razor Pages (design D6): token palette as CSS custom properties, mesh backdrop, translucent card, 8 code positions with paste-aware auto-advance and auto-submit, busy state with locked inputs, aria-invalid/aria-describedby, polite status announcements, focus moved on step change, `prefers-reduced-motion` and `@supports (backdrop-filter)` fallbacks, and the "done" confirmation echoing the address. Verify by eye against the prototype in a browser and by keyboard-only navigation through the whole flow.

## 5. End-to-end verification

- [x] 5.1 Run the full flow through the Aspire AppHost and check every spec scenario: seeded user receives a code in Mailpit and signs in; unregistered email receives/uses a code but sign-in fails and no account is created; malformed email shows validation error; resend blocked inside 60 s with remaining time shown; fourth wrong code then correct code is rejected; signed-in user skips login; sign-out requires a new code; paste-and-autosubmit works. Record results in the change notes.

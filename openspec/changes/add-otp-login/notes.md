# Implementation notes

Findings from the preview.7 research spike and the decisions they forced in tasks 3.1–3.3.

## 3.1 Invite-only enforcement

### What the public User Management surface does not offer

- The default `IOtpAuthenticator` is `Duende.UserManagement.Authentication.Otp.Internal.OtpAuthenticator`.
  Its flow is: verify the code, read `UserAuthenticators` by `OtpAddress`, create the user if the
  record is missing, then return `Success(address, subjectId)`. The create branch calls the internal
  `UserAuthenticatorsRepository.CreateAsync` directly.
- There is no public seam between "resolve the user for this OTP address" and "create the user".
  Replacing the resolution step therefore has to happen outside the default authenticator.
- `OtpAuthenticationResult.Failure` has only a private constructor; the sole way to obtain an
  instance is the internal static `Failure.Instance` singleton. `Success` is likewise not publicly
  constructible.

### Chosen wiring

- `InviteOnlyOtpAuthenticator` decorates the default authenticator. Before delegating, it reads the
  address stored for the token. If the address is known and not invited, it logs a warning and
  returns `Failure.Instance` without invoking the default authenticator, so the auto-registration
  branch is never reached. An unknown/expired token has no stored address and is delegated to the
  default authenticator, which returns its own failure. Invited addresses are delegated too: the
  pre-seeded `UserAuthenticators` record is found by address, so the create branch is not taken.
- The default authenticator is captured from the `IServiceCollection` descriptor (an internal
  implementation type with no factory) and activated with `ActivatorUtilities.CreateInstance`,
  which the spike verified works for internal cross-assembly types. The decorator is registered
  last, so last-registration-wins makes it the `IOtpAuthenticator` that DI resolves.
- `OtpChallengeAddressReader` reads `OtpWorkflowRepository.TryReadAsync(OtpToken, ct)`, a pure read
  that does not consume one of the code's four verification attempts. Using the internal
  `OtpVerifier` for this would have halved the effective attempt cap.

### Reflection is pinned to exactly two internal members

1. `OtpWorkflowRepository.TryReadAsync(OtpToken, CancellationToken)`
2. `OtpWorkflow.Address`

Everything else is public API (`IOtpAuthenticator`, `IUserProfileAdmin`,
`IUserAuthenticatorsAdmin`, `OtpAddress`, …), or is resolved from the service collection. Both
members, the workflow repository type, the default-authenticator descriptor, and
`Failure.Instance` are resolved eagerly in `AddInviteOnlyOtpLogin()` at registration time. A
preview upgrade that changes any of them fails the application at startup with an actionable
"Duende preview internals changed; invite-only OTP wiring must be updated" error instead of
silently falling back to auto-registration.

### Rejected alternative: in-memory token → address cache

The login page could have remembered the address it sent each token to and had the authenticator
consult that map. It was rejected because it duplicates state the library already stores in the OTP
workflow, the cache is per-process (wrong with multiple IdentityServer instances), entries need
their own expiry/eviction policy, and a cache miss would have to fall back to either allowing the
sign-in (unsafe) or rejecting a legitimate one.

## 3.3 Send-time decline

The spike confirmed that by-address lookup is supported: the built-in profile schema's `email`
attribute is both unique and queryable, and
`IUserProfileAdmin.TryGetAsync(AttributeCode.Create("email"), value, ct)` resolves the profile.
The login page therefore declines to dispatch a code when no profile exists for the normalized
address.

Behavior delta for the 5.1 scenario wording: "an unregistered email receives/uses a code but
sign-in fails" becomes "an unregistered email is declined at the send step; verification-layer
enforcement in `InviteOnlyOtpAuthenticator` remains the backstop for any caller that reaches the
authenticator." The error copy deliberately says the address has no account and that sign-in is
by invitation. The platform is internal and the design accepts email enumeration as a trade-off
for useful copy.

## 3.2 Development-only seed

The seed uses the administrative profile/authenticator APIs directly:

1. `IUserProfileAdmin.TryGetAsync(email)` — if the profile exists, repair only what is missing.
2. `IUserProfileAdmin.GetSchemaAsync` + `AttributeValueCollection` +
   `TryValidate` + `TryAddAsync(UserSubjectId.New(), validated, ct)` — create the profile.
3. `IUserAuthenticatorsAdmin.TryAddAsync(subjectId, [emailOtpAddress], [], ct)` — create the
   `UserAuthenticators` record. The repair path uses `TryAddOtpAddressesAsync` when the record
   already exists.

`IUserImporter` was rejected: its import path is license-gated (`ValidateRegistration`) and its
conflict resolution is designed for bulk migration, so a one-user bootstrap through it adds
configuration and failure modes without any benefit. The seed runs only inside the existing
`IsDevelopment()` block and is configuration-driven via `SeedUser:Email` / `SeedUser:Name`
(overridable with `SeedUser__Email` / `SeedUser__Name`), so demoers can use an inbox they control.

## Case-sensitivity gotcha and the normalization rule

`EmailAddress` equality is case-sensitive, and the default OTP authenticator resolves users by
`OtpAddress` equality. A profile seeded as `Demo@Example.com` with an OTP address in the same form
would not be found by a login page that sends `demo@example.com`. `OtpChannel.Create("email")` is
also not equal to `OtpChannel.Email`; only the static `OtpChannel.Email` value is correct.

Rule applied at every producer and consumer: `Trim().ToLowerInvariant()` before constructing
`EmailAddress`/`OtpAddress` or looking a user up by the `email` attribute. This is applied in the
Development seed, the login page (which also stores the normalized value in `TempData` for the OTP
step/resend), the invite check, and the resend handler. Channel comparisons use
`Equals(OtpChannel.Email)`; the class does not overload `==`.

## Build

`dotnet build Zaiware.Platform.IdentityServer/Zaiware.Platform.IdentityServer.csproj` succeeds with
0 warnings and 0 errors. Runtime verification is left to the end-to-end phase (5.1).

## E2E findings (run 1)

### TempData type-sniffing broke the code step

Symptom: every `GET /Account/EnterOtp` redirected straight back to `/Account/Login`, so the
code-entry page, server-driven countdown, and resend notice were unreachable for real users.

Root cause: the OTP token was written to `TempData` as a string, but the cookie provider's
`DefaultTempDataSerializer` type-sniffs values on deserialize. A GUID-shaped string
(`"01a201de-ec5a-76a8-bda3-abb141059c42"`) comes back as `System.Guid`, and a date-shaped string
(`"2026-11-22T06:09:36.2802994+00:00"`) comes back as `System.DateTime`. The read side used
`TempData["OtpToken"] as string`, which returned null and triggered the redirect. The cookie did
contain the values; only the casts were wrong.

Fix: the read side no longer assumes strings. The token is read as
`TempData["OtpToken"]?.ToString()` (`Guid.ToString()` produces the hyphenated form that
`OtpToken.TryCreate` accepts, verified by a successful E2E sign-in), and the blocked-until value
goes through a type-tolerant `ReadBlockedUntil` that accepts `DateTimeOffset`, `DateTime` (treated
as UTC), or an ISO-8601 string. Successful resends also clear any stale `ResendBlocked` notice.

### Postgres has no data volume in the AppHost

The AppHost's Postgres resource is not backed by a data volume, so `aspire stop` wipes the dev
database; the next full-stack start re-runs the schema migration and re-seeds the invited user.
That makes full-stack restarts useless for proving seed idempotence. Idempotence was instead
verified by restarting only the identity-server against the same still-running Postgres: the log
showed `Invited user ... already present`, no re-migration, and exactly one user row.

Optional follow-up: add a Postgres data volume in the AppHost so stopping/starting the AppHost
preserves the dev database (and makes restart-based verification possible).

## End-to-end results (task 5.1)

Combined results from the first E2E pass (run 1) and the post-fix re-test (run 2). Run 1 used
identity-server `https://localhost:27884` and MailPit HTTP `32769` / SMTP `32768`; run 2 used
identity-server `https://localhost:18687` and MailPit HTTP `32777` / SMTP `32776`.

| Scenario | Result | Evidence |
|---|---|---|
| Migration V0 → V3 and schema version | PASS | Run 1 log: `V0 → V1`, `V1 → V2`, `V2 → V3`; psql schema comment `{"Version":3}`. |
| Development seed | PASS | Run 1 log: `Seeded invited user demo@zaiware.dev`; exactly 1 profile, 1 user, 1 authenticator row. |
| MailPit web UI | PASS | `GET http://localhost:32769/` → 200. |
| Protected Index redirect | PASS | `GET /` → 302 `https://localhost:27884/Account/Login?ReturnUrl=%2F`. |
| Malformed email validation | PASS | Run 1 POST `not-an-email` → 200 `The Email field is not a valid e-mail address.`; MailPit count stayed 0. |
| Unregistered email declined | PASS | Run 1 POST `stranger@example.com` → 200 `No account found for this address...`; MailPit count stayed 0. Enforced at the send step per the 3.3 delta above. |
| EnterOtp page renders (token + countdown) | PASS | Run 2 POST demo → 302 `/Account/EnterOtp`; GET → 200 with `Enter the 8-character code sent to`, hidden UUID `01a201e9-6b82-7fb6-a388-32bd3792258c`, `data-blocked-until="2026-11-22T06:21:04Z"`. |
| Resend blocked inside 60 s with remaining time | PASS | Run 2 immediate resend → 200 page: `Please wait 45 second(s) before requesting a new code.`; `data-blocked-until` still `06:21:04Z`; MailPit count stayed 1. |
| Resend after cooldown swaps token | PASS | Run 2 after 65 s → 200 page with new hidden token `01a201ec-966a-7e56-b404-f5fb07932ca0`; MailPit 1 → 2; new code extracted. |
| Stale resend notice cleared | PASS | Run 2 successful-resend page contains zero matches for `Please wait N second(s)`. |
| Old code after resend rejected | PASS | Run 2 old token + old code → 200 `That code has expired or is not correct...`; `GET /` → 302 login. |
| Four wrong codes then correct rejected | PASS | Run 1 four × `AAAAAAAA` → error page each; fifth POST with correct code also rejected; `GET /` → 302 login. |
| Full UI-path sign-in (4.4) | PASS | Run 2 new token + new code → 302 `/`; `GET /` → 200, `You're in`, `Signed in as demo@zaiware.dev`. |
| Signed-in user skips login | PASS | Run 2 `GET /Account/Login` with signed-in jar → 302 `/` (not the email form). |
| Sign-out round trip | PASS | Run 2 POST `/Account/Logout` → 302 `/Account/Login`; `GET /` → 302 `/Account/Login?ReturnUrl=%2F`. |
| Same-DB seed idempotence | PASS | Run 1 `aspire resource identity-server restart` → `Invited user demo@zaiware.dev already present`, no migration steps, still 1/1/1 rows. |
| Full-stack restart preserves DB | N/A (known environment limitation) | A second full AppHost start re-migrated and re-seeded because the Postgres resource has no data volume; this is the environment caveat above, not a seed-code defect. |

All spec scenarios in 5.1 are PASS at the server/HTML level as exercised here. Two caveats: the
"unregistered email" scenario is enforced at the send step (the 3.3 delta recorded above), not at
code verification; and the visual/keyboard-only UI pass (4.5), including the client-side paste and
auto-submit behavior, is still pending a browser-based check.

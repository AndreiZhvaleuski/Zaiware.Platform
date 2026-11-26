# End-to-end tests

Playwright tests for the identity server, the account area, TOTP, and avatar storage.
The sandbox provides `@playwright/test` 1.61.1 and Chromium; nothing has to be installed,
and `npx` is not on PATH there — use the `playwright` executable.

## Running against a running stack

```bash
cd tests/e2e
NODE_PATH=/usr/local/share/npm-global/lib/node_modules \
PLAYWRIGHT_BROWSERS_PATH=/opt/ms-playwright \
IDENTITY_BASE_URL=https://localhost:5001 \
MAILPIT_BASE_URL=http://127.0.0.1:8025 \
AVATARS_PUBLIC_ENDPOINT=http://127.0.0.1:9000 \
playwright test
```

The suite is single-worker with no retries. It expects a freshly seeded Development
database (the Aspire AppHost, or the native stack described below). Passwords, usernames,
and the fixed TOTP secret can be overridden with the `E2E_*` environment variables read by
`support/identities.js`.

## Required services and environment

The IdentityServer reads these configuration values (environment-variable form shown):

```bash
export ASPNETCORE_ENVIRONMENT=Development
export ConnectionStrings__IdentityServer='Host=localhost;Port=5432;Database=zaiware-platform-identity-server;Username=postgres'
export ConnectionStrings__Avatars='Endpoint=http://localhost:9000;AccessKey=rustfsadmin;SecretKey=rustfsadmin;Bucket=avatars'
export Avatars__PublicEndpoint='http://localhost:9000'   # browser-visible RustFS endpoint
export DATA_PROTECTION__APPLICATION_NAME='Zaiware.Platform.IdentityServer'
export DATA_PROTECTION__KEYS_STORE_DIRECTORY='/tmp/dpkeys'
```

- PostgreSQL 17 on `5432` (the database is created by the Aspire resource; the native
  stack uses the same name).
- Mailpit with SMTP on `1025` and HTTP on `8025`; `MAILPIT_BASE_URL` defaults to
  `http://localhost:8025`. These fixed development ports are a deliberate dev limitation.
- RustFS S3 on `9000`. The `avatars` bucket must exist (Aspire's `AddBucket` provisions it;
  the native stack must create it once). The public presigned URL uses
  `Avatars:PublicEndpoint`/`AVATARS__PUBLICENDPOINT`; the S3 host port stays pinned to 9000
  so the local default `http://localhost:9000` is stable.
- HTTPS is mandatory for second-factor sign-in: `__Host-SecondFactorState` has no HTTP
  fallback. Run with the `SelfHost` launch profile (`https://localhost:5001`) or any HTTPS
  endpoint.

The Playwright process also reads `AVATARS_PUBLIC_ENDPOINT` (default `http://localhost:9000`)
to assert that the CSP `img-src` allows the same origin the server uses for presigned avatar
redirects. Keep it equal to the IdentityServer's `Avatars:PublicEndpoint`; the verified
native run used `http://127.0.0.1:9000` for both.

OTP sends have a fixed-window budget (30 per client IP and 300 globally per 10 minutes by
default; configure the `OtpSendRateLimit` section). The full suite uses about ten sends per
run, so two consecutive runs stay below the per-IP budget.

Start the IdentityServer directly with:

```bash
dotnet run --project Zaiware.Platform.IdentityServer --launch-profile SelfHost
```

## Success status flags

Account actions redirect with a short enum name in the query string (for example
`/Account?status=ProfileUpdated`), never with message text or PII. The Account page maps the
enum to the success banner; unknown values render no banner. With JavaScript, `ui.js` removes
the `status` parameter after rendering (preserving other parameters and the hash), so a
refresh does not repeat the banner. Without JavaScript the banner still renders but the
parameter remains and a refresh repeats it; `status.spec.js` documents and asserts this
trade-off. Because the flag belongs to the redirect request, one tab's save never shows a
banner in another tab.

## Test data and cleanup

The Development seed supplies the identities. E2E identities have `ResetPassword: true`,
so a restart restores their configured password, username, and display name. A configured
TOTP secret is reconciled on restart: the seed verifies the current code and rebuilds the
device when the secret changed. The demo user's password is not overwritten after a
manual change. `e2e-password-set` is seeded with `WithoutPassword: true` so the first run
can exercise the set-password flow over OTP sign-in; after that run the account has a
password, and repeat runs against the same stack exercise the change flow instead (the seed
never removes an existing password).

Mutating specs restore their state in `try/finally`; a failed run is repaired by the next
app start. The password-set identity is the exception described above. OTP resend has a
fixed one-minute server window; `otp.spec.js` waits it out when a previous run left a
cooldown. Playwright reports and `test-results/` are ignored.

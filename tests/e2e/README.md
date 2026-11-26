# End-to-end tests

Playwright tests for the identity server, the account area, and avatar storage.

The sandbox provides `@playwright/test` 1.61.1 and Chromium; nothing has to be installed.
Run against a running identity server (the Aspire AppHost or `dotnet run`):

```bash
cd tests/e2e
NODE_PATH=/usr/local/share/npm-global/lib/node_modules \
PLAYWRIGHT_BROWSERS_PATH=/opt/ms-playwright \
IDENTITY_BASE_URL=https://localhost:5001 \
MAILPIT_BASE_URL=http://localhost:8025 \
playwright test
```

The suite is single-worker and has no retries. The Development seed supplies the identities;
passwords, usernames, and the fixed TOTP secret can be overridden with the `E2E_*` environment
variables read by `support/identities.js`.

Notes:
- The identity server must be served over HTTPS so `__Host-` cookies work.
- OTP resend has a fixed one-minute server window; `otp.spec.js` waits it out when a previous
  run left a cooldown.
- Security tests clean up after themselves; a failed run can leave TOTP or a password changed.
  Restarting the Development app re-applies the seed (passwords/usernames, and TOTP is removed
  for identities without a configured secret).

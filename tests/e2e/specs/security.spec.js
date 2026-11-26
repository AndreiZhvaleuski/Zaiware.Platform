'use strict';

const { test, expect } = require('@playwright/test');
const { accountUser, securityUser, passwordChangeUser } = require('../support/identities');
const { freshTotp } = require('../support/totp');
const {
  signIn,
  signOut,
  completeTotpChallenge,
  enterRecoveryCode,
  enterTotp,
} = require('../support/flows');
const {
  expectStatusBanner,
  expectStatusRemoved,
  submitAndReadLocation,
} = require('../support/status');

async function readRecoveryCodes(page) {
  await expect(page).toHaveURL(/\/Account\/RecoveryCodes/);
  const codes = await page.locator('.recovery-codes__list code').allTextContents();

  expect(codes.length).toBeGreaterThan(0);

  const statusLocation = await submitAndReadLocation(
    page,
    () => page.locator('#recovery-done-btn').click(),
    '/Account/RecoveryCodes');

  expect(statusLocation).toContain('status=RecoveryCodesSaved');
  await expect(page).toHaveURL(/\/Account(\?status=[A-Za-z]+)?$/);
  await expectStatusBanner(page, 'Recovery codes saved.');
  await expectStatusRemoved(page);

  return codes;
}

async function readTotpSecret(page) {
  const text = await page.locator('#totp-manual-key').textContent();
  return (text || '').replace(/[^A-Za-z0-9]/g, '').toUpperCase();
}

test.describe('account security', () => {
  test('enrollment, challenges, recovery codes, regeneration, and disable', async ({ page }) => {
    test.setTimeout(180_000);

    const user = securityUser;
    let secret = null;
    let currentRecoveryCodes = [];

    try {
      await signIn(page, user.email, user.password);
      await expect(page).toHaveURL(/\/$/);

      await page.goto('/Account');
      await page.locator('#enable-totp-link').click();
      await expect(page).toHaveURL(/\/Account\/TotpSetup/);
      await expect(page.locator('#totp-code')).toHaveAttribute('aria-describedby', /code-error/);
      await expect(page.locator('#totp-code')).toHaveAttribute('aria-describedby', /setup-errors/);

      secret = await readTotpSecret(page);
      expect(secret.length).toBeGreaterThanOrEqual(16);

      await page.locator('#totp-code').fill(await freshTotp(secret));
      await page.locator('#totp-confirm-btn').click();

      const codesA = await readRecoveryCodes(page);
      currentRecoveryCodes = codesA;
      await expect(page.locator('#totp-status')).toContainText('Enabled');
      await expect(page.locator('#recovery-count')).toHaveText(String(codesA.length));

      // While enrolled, visiting setup again reports the already-enabled outcome once.
      const setupLocation = await submitAndReadLocation(
        page,
        () => page.goto('/Account/TotpSetup'),
        '/Account/TotpSetup',
        'GET');
      expect(setupLocation).toContain('status=TwoFactorAlreadyEnabled');
      await expectStatusBanner(page, 'Two-factor authentication is already enabled.');
      await expectStatusRemoved(page);

      // The codes are shown exactly once; a later visit returns to the account page.
      await page.goto('/Account/RecoveryCodes');
      await expect(page).toHaveURL(/\/Account$/);

      // Sign out and sign in again: the challenge must appear and complete.
      await signOut(page);
      await signIn(page, user.email, user.password);
      // The enrollment step used the current window; use the next one to avoid replay rejection.
      // Enrollment already consumed the current step; use the server-tolerated next
      // step so replay protection does not reject the challenge.
      await completeTotpChallenge(page, await freshTotp(secret, 1));
      await expect(page.locator('#email-done')).toHaveText(user.email);

      // Regenerate: every code from the first set must stop working.
      await page.goto('/Account');
      await page.locator('#regenerate-codes-btn').click();
      const codesB = await readRecoveryCodes(page);
      currentRecoveryCodes = codesB;

      await signOut(page);
      await signIn(page, user.email, user.password);
      await expect(page).toHaveURL(/\/Account\/TwoFactor/);
      await expect(page.locator('#recovery-code')).toHaveAttribute('aria-describedby', /recovery-error/);
      await expect(page.locator('#recovery-code')).toHaveAttribute('aria-describedby', /twofactor-error/);

      await enterRecoveryCode(page, codesA[0]);
      await expect(page).toHaveURL(/\/Account\/TwoFactor/);
      await expect(page.locator('.notice.notice--error')).toContainText("isn't valid");

      await enterRecoveryCode(page, codesB[0]);
      await expect(page).toHaveURL(/\/$/, { timeout: 15_000 });

      // A used recovery code cannot be reused.
      await signOut(page);
      await signIn(page, user.email, user.password);
      await expect(page).toHaveURL(/\/Account\/TwoFactor/);

      await enterRecoveryCode(page, codesB[0]);
      await expect(page).toHaveURL(/\/Account\/TwoFactor/);
      await expect(page.locator('.notice.notice--error')).toContainText("isn't valid");

      await enterRecoveryCode(page, codesB[1]);
      await expect(page).toHaveURL(/\/$/, { timeout: 15_000 });

      // Disable: the next password sign-in must not challenge.
      await page.goto('/Account');
      const disableLocation = await submitAndReadLocation(
        page,
        () => page.locator('#disable-totp-btn').click(),
        '/Account?handler=DisableTotp');
      expect(disableLocation).toContain('status=TwoFactorDisabled');
      await expectStatusBanner(page, 'Two-factor authentication disabled.');
      await expectStatusRemoved(page);

      await signOut(page);
      await signIn(page, user.email, user.password);
      await expect(page).toHaveURL(/\/$/);
      await expect(page.locator('#email-done')).toHaveText(user.email);
    } finally {
      // Robust cleanup: a failure at the challenge page (after signing out in the test)
      // must not leave the disposable identity enrolled for the next run. Complete the
      // half-auth challenge with the secret this test enrolled, then disable the factor.
      const completeChallenge = async () => {
        if (!page.url().includes('/Account/TwoFactor')) {
          return;
        }

        if (secret) {
          for (const offset of [0, 1]) {
            try {
              await enterTotp(page, await freshTotp(secret, offset));
              await expect(page).toHaveURL(/\/$/, { timeout: 15_000 });
              return;
            } catch {
              if (!page.url().includes('/Account/TwoFactor')) {
                return;
              }
            }
          }
        }

        // If the TOTP step cannot be used (for example a consumed window), recovery
        // codes from the current set are the deterministic fallback.
        for (const code of [...currentRecoveryCodes].reverse()) {
          try {
            await enterRecoveryCode(page, code);
            if (page.url().endsWith('/')) {
              return;
            }
          } catch {
            // Try the next candidate.
          }
        }
      };

      try {
        await completeChallenge();
        await page.goto('/Account');

        // The test may have failed while signed out or between sessions.
        if (page.url().includes('/Account/Login')) {
          await signIn(page, user.email, user.password);
          await completeChallenge();
          await page.goto('/Account');
        }

        if (await page.locator('#disable-totp-btn').count() > 0) {
          await page.locator('#disable-totp-btn').click();
          await expect(page.locator('.notice--success')).toContainText('disabled');
        }
      } catch {
        // Best effort: the next app start reconciles the seeded TOTP state for this
        // identity, and this test's final state is verified on a fresh stack anyway.
      }
    }
  });

  test('abandoning enrollment before confirmation does not enable two-factor', async ({ page }) => {
    const user = accountUser;

    await signIn(page, user.email, user.password);
    await page.goto('/Account');
    await page.locator('#enable-totp-link').click();
    await expect(page).toHaveURL(/\/Account\/TotpSetup/);

    // Read the pending key but never confirm it.
    expect((await readTotpSecret(page)).length).toBeGreaterThan(0);

    await signOut(page);
    await signIn(page, user.email, user.password);

    await expect(page).toHaveURL(/\/$/);
    await expect(page.locator('#email-done')).toHaveText(user.email);
  });

  test('a password change takes effect and the old password fails', async ({ page }) => {
    test.setTimeout(120_000);

    const user = passwordChangeUser;
    const newPassword = 'E2e-Password-Change-2026-New!';
    let currentPassword = user.password;

    await signIn(page, user.email, currentPassword);

    // Self-heal a previous run that left the alternate password in place.
    if (page.url().includes('/Account/Login')) {
      currentPassword = newPassword;
      await signIn(page, user.email, currentPassword);
    }

    const nextPassword = currentPassword === user.password ? newPassword : user.password;

    try {
      await page.goto('/Account');
      await page.locator('#current-password').fill(currentPassword);
      await page.locator('#new-password').fill(nextPassword);
      await page.locator('#confirm-password').fill(nextPassword);

      const location = await submitAndReadLocation(
        page,
        () => page.locator('#password-save-btn').click(),
        '/Account?handler=Password');
      expect(location).toContain('status=PasswordChanged');
      await expectStatusBanner(page, 'Password changed.');
      await expectStatusRemoved(page);

      await signOut(page);
      await signIn(page, user.email, nextPassword);
      await expect(page).toHaveURL(/\/$/);

      await signOut(page);
      await signIn(page, user.email, currentPassword);
      await expect(page).toHaveURL(/\/Account\/Login/);
      await expect(page.locator('.notice.notice--error').first()).toContainText('Invalid username or password');
    } finally {
      // Restore the seeded password even if the assertions above failed.
      try {
        await signIn(page, user.email, nextPassword);

        if (!page.url().includes('/Account/Login') && nextPassword !== user.password) {
          await page.goto('/Account');
          await page.locator('#current-password').fill(nextPassword);
          await page.locator('#new-password').fill(user.password);
          await page.locator('#confirm-password').fill(user.password);
          await page.locator('#password-save-btn').click();
          await expect(page.locator('.notice--success')).toContainText('Password changed');
        }
      } catch {
        // A fresh-stack seed reset restores the configured password.
      }
    }
  });
});

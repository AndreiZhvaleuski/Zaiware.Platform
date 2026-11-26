'use strict';

const { test, expect } = require('@playwright/test');
const { totpUser } = require('../support/identities');
const { totp } = require('../support/totp');

async function signInToChallenge(page, account) {
  await page.goto('/Account/Login');
  await page.locator('#identifier').fill(account.username);
  await page.locator('#password').fill(account.password);
  await page.locator('#signin-btn').click();
  await expect(page).toHaveURL(/\/Account\/TwoFactor/);
}

async function enterTotp(page, code) {
  const digits = page.locator('[data-code-digit]');

  for (let index = 0; index < code.length; index += 1) {
    await digits.nth(index).fill(code[index]);
  }
}

test.describe('TOTP second factor', () => {
  test('challenges an enrolled user and completes with the current code', async ({ page }) => {
    await signInToChallenge(page, totpUser);
    await expect(page.locator('#title')).toHaveText('Two-step verification');

    // Compute the code for the current window; allow a one-step clock skew.
    await enterTotp(page, totp(totpUser.secret));

    await expect(page).toHaveURL(/\/$/, { timeout: 15_000 });
    await expect(page.locator('#email-done')).toHaveText(totpUser.email);
  });

  test('rejects a wrong authenticator code without establishing a session', async ({ page }) => {
    await signInToChallenge(page, totpUser);
    await enterTotp(page, '000000');

    await expect(page).toHaveURL(/\/Account\/TwoFactor/);
    await expect(page.locator('.notice.notice--error')).toContainText("That code isn't valid");

    await page.goto('/');
    await expect(page).toHaveURL(/\/Account\/Login/);
  });

  test('direct navigation to the challenge bounces to sign-in', async ({ page }) => {
    await page.goto('/Account/TwoFactor');

    await expect(page).toHaveURL(/\/Account\/Login/);
    await expect(page.locator('.notice.notice--error').first()).toContainText('session expired');
  });
});

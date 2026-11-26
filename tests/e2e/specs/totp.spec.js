'use strict';

const { test, expect } = require('@playwright/test');
const { totpUser } = require('../support/identities');
const { freshTotp, wrongTotp } = require('../support/totp');

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

    await enterTotp(page, await freshTotp(totpUser.secret));
    await page.waitForTimeout(1200);

    // Retry once with a fresh code if the first submit landed on a window boundary.
    if (!page.url().endsWith('/')) {
      await expect(page).toHaveURL(/\/Account\/TwoFactor/);
      await enterTotp(page, await freshTotp(totpUser.secret));
    }

    await expect(page).toHaveURL(/\/$/, { timeout: 15_000 });
    await expect(page.locator('#email-done')).toHaveText(totpUser.email);
  });

  test('rejects a wrong authenticator code without establishing a session', async ({ page }) => {
    await signInToChallenge(page, totpUser);
    await enterTotp(page, wrongTotp(totpUser.secret));

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

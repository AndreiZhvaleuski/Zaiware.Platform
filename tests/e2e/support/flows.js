'use strict';

const { expect } = require('@playwright/test');

async function signIn(page, identifier, password) {
  await page.goto('/Account/Login');
  await page.locator('#identifier').fill(identifier);
  await page.locator('#password').fill(password);
  await page.locator('#signin-btn').click();
  await page.waitForLoadState('networkidle');
}

async function signOut(page) {
  await page.goto('/Account');
  await page.locator('#account-signout-btn').click();
  await expect(page).toHaveURL(/\/Account\/Login/);
}

async function enterTotp(page, code) {
  const digits = page.locator('[data-code-digit]');

  for (let index = 0; index < code.length; index += 1) {
    await digits.nth(index).fill(code[index]);
  }
}

async function enterRecoveryCode(page, code) {
  await page.locator('.twofactor__recovery summary').click();
  await page.locator('#recovery-code').fill(code);
  await page.locator('#verify-recovery-btn').click();
  await page.waitForLoadState('networkidle');
}

async function completeTotpChallenge(page, code) {
  await expect(page).toHaveURL(/\/Account\/TwoFactor/);
  await enterTotp(page, code);
  await expect(page).toHaveURL(/\/$/, { timeout: 15_000 });
}

module.exports = { signIn, signOut, enterTotp, enterRecoveryCode, completeTotpChallenge };

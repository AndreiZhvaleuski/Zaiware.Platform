'use strict';

const { test, expect } = require('@playwright/test');
const { passwordUser: PASSWORD_USER } = require('../support/identities');

async function signInWithPassword(page, identifier, password) {
  await page.goto('/Account/Login');
  await page.locator('#identifier').fill(identifier);
  await page.locator('#password').fill(password);
  await page.locator('#signin-btn').click();
  await page.waitForLoadState('networkidle');
}

test.describe('password sign-in', () => {
  test('signs in with the username and returns to the app', async ({ page }) => {
    await signInWithPassword(page, PASSWORD_USER.username, PASSWORD_USER.password);

    await expect(page).toHaveURL(/\/$/);
    await expect(page.locator('#email-done')).toHaveText(PASSWORD_USER.email);
  });

  test('signs in with the email address', async ({ page }) => {
    await signInWithPassword(page, PASSWORD_USER.email, PASSWORD_USER.password);

    await expect(page).toHaveURL(/\/$/);
    await expect(page.locator('#email-done')).toHaveText(PASSWORD_USER.email);
  });

  test('uses one generic message for an unknown user and a wrong password', async ({ page }) => {
    await signInWithPassword(page, 'no-such-person', 'Wrong-Password-2026!');

    await expect(page).toHaveURL(/\/Account\/Login/);
    const unknownNotice = page.locator('.notice.notice--error').first();
    await expect(unknownNotice).toContainText('Invalid username or password.');
    const unknownText = await unknownNotice.textContent();

    await signInWithPassword(page, PASSWORD_USER.username, 'Wrong-Password-2026!');
    await expect(page).toHaveURL(/\/Account\/Login/);
    const wrongPasswordNotice = page.locator('.notice.notice--error').first();
    await expect(wrongPasswordNotice).toContainText('Invalid username or password.');

    expect(await wrongPasswordNotice.textContent()).toBe(unknownText);
  });

  test('exposes autofill tokens on the combined screen', async ({ page }) => {
    await page.goto('/Account/Login');

    await expect(page.locator('#identifier')).toHaveAttribute('autocomplete', 'username');
    await expect(page.locator('#password')).toHaveAttribute('autocomplete', 'current-password');
    await expect(page.locator('#email')).toHaveAttribute('autocomplete', 'email');
    await expect(page.locator('#password-form')).toBeVisible();
    await expect(page.locator('#email-form')).toBeVisible();
  });
});

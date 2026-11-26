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

  for (const [label, identifier, password] of [
    ['empty', '', ''],
    ['whitespace-only', '   ', '   '],
  ]) {
    test(`rejects ${label} credentials with a validation page, never a 500`, async ({ page }) => {
      await page.goto('/Account/Login');
      await page.locator('#identifier').fill(identifier);
      await page.locator('#password').fill(password);

      const responsePromise = page.waitForResponse((response) =>
        response.url().includes('/Account/Login') && response.request().method() === 'POST');
      await page.locator('#signin-btn').click();
      const response = await responsePromise;

      expect(response.status()).toBe(200);
      await expect(page).toHaveURL(/\/Account\/Login/);
      await expect(page.locator('#identifier-error')).toContainText('Enter your username or email');
      await expect(page.locator('#password-error')).toContainText('Enter your password');
    });

    test(`rejects ${label} one-time-code email with a validation page, never a 500`, async ({ page }) => {
      await page.goto('/Account/Login');
      await page.locator('#email').fill(identifier === '   ' ? '   ' : '');

      const responsePromise = page.waitForResponse((response) =>
        response.url().includes('/Account/Login') && response.request().method() === 'POST');
      await page.locator('#send-btn').click();
      const response = await responsePromise;

      expect(response.status()).toBe(200);
      await expect(page).toHaveURL(/\/Account\/Login/);
      await expect(page.locator('#email-error')).toContainText('looks off');
    });
  }

  test('a signed-in user skips the login screen', async ({ page }) => {
    await signInWithPassword(page, PASSWORD_USER.username, PASSWORD_USER.password);
    await expect(page).toHaveURL(/\/$/);

    await page.goto('/Account/Login');
    await expect(page).toHaveURL(/\/$/);
    await expect(page.locator('#email-done')).toHaveText(PASSWORD_USER.email);
  });

  test('protected pages redirect to sign-in after sign-out', async ({ page }) => {
    await signInWithPassword(page, PASSWORD_USER.username, PASSWORD_USER.password);
    await page.goto('/Account');
    await page.locator('#account-signout-btn').click();
    await expect(page).toHaveURL(/\/Account\/Login/);

    await page.goto('/Account');
    await expect(page).toHaveURL(/\/Account\/Login/);
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

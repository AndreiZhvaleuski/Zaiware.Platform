'use strict';

const { test, expect } = require('@playwright/test');
const { accountUser, passwordUser } = require('../support/identities');
const { signIn, signOut } = require('../support/flows');

test.describe('account profile', () => {
  test('shows the email read-only and offers no email edit control', async ({ page }) => {
    await signIn(page, accountUser.email, accountUser.password);
    await page.goto('/Account');

    await expect(page.locator('#account-email')).toHaveText(accountUser.email);
    await expect(page.locator('input[type="email"]')).toHaveCount(0);
    await expect(page.locator('input[name*="Email"]')).toHaveCount(0);
  });

  test('changes the username, persists the display name, and signs in with the new username', async ({ page }) => {
    await signIn(page, accountUser.email, accountUser.password);
    await page.goto('/Account');

    try {
      await page.locator('#account-username').fill('e2e-account-renamed');
      await page.locator('#account-display-name').fill('Renamed Account');
      await page.locator('#profile-save-btn').click();
      await expect(page.locator('.notice--success')).toContainText('Profile updated');

      await page.reload();
      await expect(page.locator('#account-username')).toHaveValue('e2e-account-renamed');
      await expect(page.locator('#account-display-name')).toHaveValue('Renamed Account');

      await signOut(page);
      await signIn(page, 'e2e-account-renamed', accountUser.password);
      await expect(page).toHaveURL(/\/$/);
    } finally {
      // Restore the seeded values even when an assertion above failed, so later specs
      // do not inherit the renamed identity.
      try {
        await page.goto('/Account');
        if (await page.locator('#account-username').count() > 0) {
          await page.locator('#account-username').fill(accountUser.username);
          await page.locator('#account-display-name').fill('E2E Account');
          await page.locator('#profile-save-btn').click();
          await expect(page.locator('.notice--success')).toContainText('Profile updated');
        }
      } catch {
        // The seed repairs usernames and display names on the next app start.
      }
    }
  });

  test('rejects a duplicate username and keeps the existing one', async ({ page }) => {
    await signIn(page, accountUser.email, accountUser.password);
    await page.goto('/Account');

    const current = await page.locator('#account-username').inputValue();
    await page.locator('#account-username').fill(passwordUser.username);
    await page.locator('#account-display-name').fill('Duplicate Attempt');
    await page.locator('#profile-save-btn').click();

    await expect(page.locator('#username-error')).toContainText('already taken');
    await page.goto('/Account');
    await expect(page.locator('#account-username')).toHaveValue(current);
  });

  test('rejects a weak password with policy details', async ({ page }) => {
    await signIn(page, passwordUser.email, passwordUser.password);
    await page.goto('/Account');

    await page.locator('#current-password').fill(passwordUser.password);
    await page.locator('#new-password').fill('short');
    await page.locator('#confirm-password').fill('short');
    await page.locator('#password-save-btn').click();

    await expect(page.locator('.notice--error')).toContainText(/15/);

    // The profile form must not be blank after a failed security action.
    await expect(page.locator('#account-username')).toHaveValue(passwordUser.username);
    await expect(page.locator('#account-display-name')).toHaveValue('E2E Password');
    await expect(page.locator('#new-password')).toHaveAttribute('aria-invalid', 'true');
    await expect(page.locator('#new-password')).toHaveAttribute('aria-describedby', /security-errors/);

    await signOut(page);
    await signIn(page, passwordUser.email, passwordUser.password);
    await expect(page).toHaveURL(/\/$/);
  });
});

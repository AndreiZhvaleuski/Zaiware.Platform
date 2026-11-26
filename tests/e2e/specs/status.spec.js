'use strict';

const { test, expect } = require('@playwright/test');
const { accountUser, passwordUser, passwordSetUser } = require('../support/identities');
const { signIn } = require('../support/flows');
const { clearMailbox, latestOtpTo } = require('../support/mailpit');
const {
  expectNoStatusBanner,
  expectStatusBanner,
  expectStatusRemoved,
  submitAndReadLocation,
} = require('../support/status');

test.setTimeout(120_000);

async function waitForOtp(email) {
  await expect.poll(async () => {
    try {
      return await latestOtpTo(email);
    } catch {
      return '';
    }
  }, { timeout: 20_000 }).not.toBe('');

  return latestOtpTo(email);
}

async function signInWithOtp(page, email) {
  await clearMailbox();
  await page.goto('/Account/Login');
  await page.locator('#email').fill(email);
  await page.locator('#send-btn').click();
  await expect(page).toHaveURL(/\/Account\/EnterOtp/);

  const code = await waitForOtp(email);
  const digits = page.locator('[data-code-digit]');

  for (let index = 0; index < code.length; index += 1) {
    await digits.nth(index).evaluate((element, value) => {
      element.value = value;
    }, code[index]);
  }

  const responsePromise = page.waitForResponse((response) =>
    response.url().includes('/Account/EnterOtp') && response.request().method() === 'POST');
  await page.locator('#otp-form').evaluate((form) => form.requestSubmit());
  await responsePromise;
  await page.waitForLoadState('domcontentloaded');
}

test.describe('account status flags', () => {
  test('a profile save shows the banner once and cleans the status parameter', async ({ page }) => {
    await signIn(page, accountUser.email, accountUser.password);
    await page.goto('/Account');
    const displayName = await page.locator('#account-display-name').inputValue();

    const location = await submitAndReadLocation(
      page,
      () => page.locator('#profile-save-btn').click(),
      '/Account?handler=Profile');

    expect(location).toContain('status=ProfileUpdated');
    await expectStatusBanner(page, 'Profile updated.');
    await expectStatusRemoved(page);

    await page.reload();
    await expect(page.locator('#account-display-name')).toHaveValue(displayName);
    await expectNoStatusBanner(page);
  });

  test('a save in one tab does not leak the banner into another tab', async ({ page, context }) => {
    await signIn(page, accountUser.email, accountUser.password);
    const tabA = await context.newPage();
    await tabA.goto('/Account');
    await expectNoStatusBanner(tabA);

    try {
      await page.goto('/Account');
      await page.locator('#account-display-name').fill('E2E Account Tab B');

      const location = await submitAndReadLocation(
        page,
        () => page.locator('#profile-save-btn').click(),
        '/Account?handler=Profile');

      expect(location).toContain('status=ProfileUpdated');
      await expectStatusBanner(page, 'Profile updated.');
      await expectStatusRemoved(page);

      // Tab A was opened before the save and has no status flag of its own. Reloading it
      // shows the new profile but no success banner.
      await tabA.reload();
      await expect(tabA.locator('#account-display-name')).toHaveValue('E2E Account Tab B');
      await expectNoStatusBanner(tabA);
      await expectStatusRemoved(tabA);
    } finally {
      // Restore the seeded display name for later specs and runs.
      try {
        await page.goto('/Account');
        await page.locator('#account-display-name').fill('E2E Account');
        await page.locator('#profile-save-btn').click();
        await expectStatusBanner(page, 'Profile updated.');
        await expectStatusRemoved(page);
      } catch {
        // The Development seed repairs the display name on the next app start.
      }

      await tabA.close();
    }
  });

  test('setting or changing a password reports the matching status', async ({ page }) => {
    await signInWithOtp(page, passwordSetUser.email);
    await expect(page).toHaveURL(/\/$/);
    await page.goto('/Account');

    const hasPassword = await page.locator('#current-password').count() > 0;

    if (hasPassword) {
      // A previous run set the password; exercise the change path with the same known value.
      await page.locator('#current-password').fill(passwordSetUser.password);
    }

    await page.locator('#new-password').fill(passwordSetUser.password);
    await page.locator('#confirm-password').fill(passwordSetUser.password);

    const location = await submitAndReadLocation(
      page,
      () => page.locator('#password-save-btn').click(),
      '/Account?handler=Password');

    expect(location).toContain(hasPassword ? 'status=PasswordChanged' : 'status=PasswordSet');
    await expectStatusBanner(page, hasPassword ? 'Password changed.' : 'Password set.');
    await expectStatusRemoved(page);

    await page.reload();
    await expectNoStatusBanner(page);
    await expect(page.locator('#current-password')).toHaveCount(1);
  });

  test('an unknown status renders no banner and is cleaned from the URL', async ({ page }) => {
    await signIn(page, passwordUser.email, passwordUser.password);
    await page.goto('/Account?status=DefinitelyNotAStatus');

    await expectNoStatusBanner(page);
    await expect(page.locator('#account-username')).toHaveValue(passwordUser.username);
    await expectStatusRemoved(page);
  });

  test('without JavaScript the banner renders and the status parameter remains', async ({ browser }) => {
    const context = await browser.newContext({ javaScriptEnabled: false, ignoreHTTPSErrors: true });
    const page = await context.newPage();

    await signIn(page, passwordUser.email, passwordUser.password);
    await page.goto('/Account?status=ProfileUpdated');

    // Documented progressive-enhancement trade-off: no URL cleanup without JavaScript, so
    // a refresh repeats the (non-sensitive) success banner.
    await expectStatusBanner(page, 'Profile updated.');
    expect(new URL(page.url()).searchParams.get('status')).toBe('ProfileUpdated');

    await page.reload();
    await expectStatusBanner(page, 'Profile updated.');
    expect(new URL(page.url()).searchParams.get('status')).toBe('ProfileUpdated');

    await context.close();
  });
});

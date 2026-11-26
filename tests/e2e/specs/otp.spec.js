'use strict';

const { test, expect } = require('@playwright/test');
const { clearMailbox, latestOtpTo } = require('../support/mailpit');
const { passwordUser, totpUser, demoUser, accountUser } = require('../support/identities');

test.setTimeout(120_000);

async function requestCode(page, email) {
  await clearMailbox();
  await page.goto('/Account/Login');
  await page.locator('#email').fill(email);
  await page.locator('#send-btn').click();

  const blocked = page.locator('.notice.notice--error, .field__error').filter({ hasText: 'Please wait' });

  if (await blocked.count() > 0) {
    // The built-in OTP resend interval is fixed at one minute. Wait out the remainder so
    // reruns of the suite are deterministic rather than failing on a leftover cooldown.
    const text = await blocked.first().textContent();
    const seconds = Number((text.match(/wait (\d+) second/i) || [])[1] || 60);
    await page.waitForTimeout((seconds + 1) * 1000);
    await page.locator('#send-btn').click();
  }

  await expect(page).toHaveURL(/\/Account\/EnterOtp/);
}

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

async function submitCode(page, code) {
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
  // Do not wait for networkidle: the post-sign-in Account page loads the avatar route,
  // which can keep a connection open longer than the assertion needs.
  await page.waitForLoadState('domcontentloaded');
}

test.describe('one-time-code sign-in', () => {
  test('completes the round trip, honours the server cooldown, and accepts a pasted code', async ({ page }) => {
    await requestCode(page, passwordUser.email);
    await expect(page.locator('.otp__email')).toHaveText(passwordUser.email);

    const blockedUntil = page.locator('#resend');
    await expect(blockedUntil).toBeDisabled();
    const blockedUntilValue = await blockedUntil.getAttribute('data-blocked-until');
    expect(Date.parse(blockedUntilValue)).toBeGreaterThan(Date.now());
    expect(Date.parse(blockedUntilValue) - Date.now()).toBeLessThanOrEqual(65_000);
    await expect(blockedUntil).toContainText('Resend in');

    // A resend inside the server window is refused with the server's notice. The button is
    // disabled by the enhancement, so exercise the server contract directly.
    const antiforgery = await page.locator('#otp-form input[name="__RequestVerificationToken"]').inputValue();
    const resend = await page.request.post('/Account/EnterOtp?handler=Resend', {
      form: { __RequestVerificationToken: antiforgery },
      maxRedirects: 0,
    });
    expect(resend.status()).toBe(302);
    await page.goto(resend.headers()['location']);
    await expect(page.locator('.field__error, .notice').filter({ hasText: 'Please wait' })).toBeVisible();

    const code = await waitForOtp(passwordUser.email);

    // Paste the whole code into the first position; the enhancement fills the rest and submits.
    await page.locator('[data-code-digit]').first().evaluate((element, value) => {
      element.value = value;
      element.dispatchEvent(new Event('input', { bubbles: true }));
    }, code);

    await expect(page).toHaveURL(/\/$/, { timeout: 15_000 });
    await expect(page.locator('#email-done')).toHaveText(passwordUser.email);
  });

  test('honours a local return URL after OTP verification', async ({ page }) => {
    await clearMailbox();
    await page.goto('/Account/Login?returnUrl=%2FAccount');
    await page.locator('#email').fill(accountUser.email);
    await page.locator('#send-btn').click();
    await expect(page).toHaveURL(/\/Account\/EnterOtp/);

    const code = await waitForOtp(accountUser.email);
    await submitCode(page, code);

    await expect(page).toHaveURL(/\/Account$/);
    await expect(page.locator('#account-email')).toHaveText(accountUser.email);
  });

  test('drops a malicious return URL instead of redirecting off-site', async ({ page }) => {
    await clearMailbox();
    await page.goto('/Account/Login?returnUrl=https%3A%2F%2Fevil.example%2F');
    await page.locator('#identifier').fill(passwordUser.username);
    await page.locator('#password').fill(passwordUser.password);
    await page.locator('#signin-btn').click();

    await expect(page).toHaveURL(/\/$/);
    expect(page.url()).not.toContain('evil.example');
  });

  test('changes the address by returning to the combined sign-in screen', async ({ page }) => {
    await requestCode(page, demoUser.email);
    await page.locator('.back-link').click();

    await expect(page).toHaveURL(/\/Account\/Login/);
    await expect(page.locator('#email')).toHaveValue(demoUser.email);
    await expect(page.locator('#identifier')).toBeVisible();
  });

  test('a malformed address is rejected locally without dispatching', async ({ page }) => {
    await page.goto('/Account/Login');
    await page.locator('#email').fill('not-an-email');

    const responsePromise = page.waitForResponse((response) =>
      response.url().includes('/Account/Login') && response.request().method() === 'POST');
    await page.locator('#send-btn').click();
    const response = await responsePromise;

    expect(response.status()).toBe(200);
    await expect(page).toHaveURL(/\/Account\/Login/);
    await expect(page.locator('#email-error')).toContainText('looks off');
    await expect(page).not.toHaveURL(/\/Account\/EnterOtp/);
  });

  test('an unknown but valid address gets a code and cannot complete sign-in', async ({ page }) => {
    const unknown = `unknown-${Date.now()}@zaiware.dev`;

    // Anti-enumeration: the page must not reveal that no account exists. The same
    // response and the code-entry step are shown for unknown addresses.
    await requestCode(page, unknown);
    await expect(page.locator('.otp__email')).toHaveText(unknown);

    const code = await waitForOtp(unknown);
    await submitCode(page, code);

    await expect(page).toHaveURL(/\/Account\/EnterOtp/);
    await expect(page.locator('#otp-error, .notice.notice--error').first()).toBeVisible();

    await page.goto('/');
    await expect(page).toHaveURL(/\/Account\/Login/);
  });

  test('resending invalidates the previous code', async ({ page }) => {
    test.setTimeout(180_000);

    await requestCode(page, passwordUser.email);
    const oldCode = await waitForOtp(passwordUser.email);

    const resend = page.locator('#resend');
    const blockedUntil = await resend.getAttribute('data-blocked-until');
    const waitMs = Math.max(0, Date.parse(blockedUntil) - Date.now()) + 1500;
    await page.waitForTimeout(waitMs);
    await resend.click();
    await expect(page).toHaveURL(/\/Account\/EnterOtp/);

    await expect.poll(async () => {
      try {
        const candidate = await latestOtpTo(passwordUser.email);
        return candidate !== oldCode ? candidate : '';
      } catch {
        return '';
      }
    }, { timeout: 20_000 }).not.toBe('');

    await submitCode(page, oldCode);
    await expect(page).toHaveURL(/\/Account\/EnterOtp/);
    await expect(page.locator('#otp-error, .notice.notice--error').first()).toBeVisible();
  });

  test('four wrong attempts invalidate the code', async ({ page }) => {
    await requestCode(page, totpUser.email);
    const code = await waitForOtp(totpUser.email);
    const wrong = code === 'AAAAAAAA' ? 'BBBBBBBB' : 'AAAAAAAA';

    for (let attempt = 0; attempt < 4; attempt += 1) {
      await submitCode(page, wrong);
      await expect(page.locator('#otp-form')).toBeVisible();
    }

    await submitCode(page, code);

    await expect(page).toHaveURL(/\/Account\/EnterOtp/);
    await expect(page.locator('#otp-error, .notice.notice--error').first()).toBeVisible();
    await page.goto('/');
    await expect(page).toHaveURL(/\/Account\/Login/);
  });
});

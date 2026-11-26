'use strict';

const { test, expect } = require('@playwright/test');
const { clearMailbox, latestOtpTo } = require('../support/mailpit');
const { passwordUser, totpUser, demoUser } = require('../support/identities');

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
  await page.waitForLoadState('networkidle');
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

  test('changes the address by returning to the combined sign-in screen', async ({ page }) => {
    await requestCode(page, demoUser.email);
    await page.locator('.back-link').click();

    await expect(page).toHaveURL(/\/Account\/Login/);
    await expect(page.locator('#email')).toHaveValue(demoUser.email);
    await expect(page.locator('#identifier')).toBeVisible();
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

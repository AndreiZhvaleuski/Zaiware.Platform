'use strict';

const { test, expect } = require('@playwright/test');

test.describe('ui foundations', () => {
  test('system theme applies before the first paint when no override is stored', async ({ page }) => {
    await page.emulateMedia({ colorScheme: 'dark' });
    await page.goto('/Account/Login');

    await expect(page.locator('html')).not.toHaveAttribute('data-theme', /.+/);
    await expect(page.locator('body')).toHaveCSS('background-color', 'rgb(10, 13, 19)');
  });

  test('manual theme override persists across reloads', async ({ page }) => {
    await page.goto('/Account/Login');

    const toggle = page.locator('#theme-toggle');
    await expect(toggle).toBeVisible();

    const initialTheme = await page.locator('html').getAttribute('data-theme');
    await toggle.click();

    const firstTheme = await page.locator('html').getAttribute('data-theme');
    expect(firstTheme).toMatch(/^(light|dark)$/);
    expect(firstTheme).not.toBe(initialTheme);
    await expect.poll(() => page.evaluate(() => localStorage.getItem('theme'))).toBe(firstTheme);

    await page.reload();
    await expect(page.locator('html')).toHaveAttribute('data-theme', firstTheme);

    await toggle.click();
    const secondTheme = firstTheme === 'dark' ? 'light' : 'dark';
    await page.reload();
    await expect(page.locator('html')).toHaveAttribute('data-theme', secondTheme);
  });

  test('stored override is applied before styles during load', async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem('theme', 'dark'));
    await page.goto('/Account/Login');

    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
    await expect(page.locator('body')).toHaveCSS('background-color', 'rgb(10, 13, 19)');
  });

  test('sign-in and account pages send no-store cache headers', async ({ page }) => {
    const response = await page.goto('/Account/Login');
    expect(response).not.toBeNull();

    const cacheControl = (await response.headers())['cache-control'] || '';
    expect(cacheControl.toLowerCase()).toContain('no-store');
  });

  test('stylesheets are referenced with content versioning', async ({ page }) => {
    await page.goto('/Account/Login');

    const hrefs = await page.locator('link[rel="stylesheet"]').evaluateAll((links) =>
      links.map((link) => link.getAttribute('href') || ''));

    expect(hrefs.length).toBeGreaterThanOrEqual(3);

    for (const href of hrefs) {
      expect(href).toMatch(/(\?v=[A-Za-z0-9_-]+|[a-z0-9]{8,}(\.[a-z0-9]+)?\.css)/);
    }

    expect(hrefs.some((href) => /\/css\/tokens(\.|\.css\?v=)/.test(href))).toBe(true);
    expect(hrefs.some((href) => /\/css\/primitives(\.|\.css\?v=)/.test(href))).toBe(true);
    expect(hrefs.some((href) => href.includes('.styles.css'))).toBe(true);
  });

  test('fingerprinted assets are served as immutable', async ({ page }) => {
    await page.goto('/Account/Login');

    const hrefs = await page.locator('link[rel="stylesheet"]').evaluateAll((links) =>
      links.map((link) => link.getAttribute('href') || ''));

    for (const href of hrefs) {
      const response = await page.request.get(new URL(href, page.url()).toString());
      expect(response.status()).toBe(200);
      expect((response.headers()['cache-control'] || '').toLowerCase()).toContain('immutable');
    }
  });

  test('the deferred script is content-versioned and immutable', async ({ page }) => {
    await page.goto('/Account/Login');

    const src = await page.locator('script[src]').first().getAttribute('src');
    expect(src).toBeTruthy();
    expect(src).toMatch(/(\?v=[A-Za-z0-9_-]+|[a-z0-9]{8,}\.js)/);

    const response = await page.request.get(new URL(src, page.url()).toString());
    expect(response.status()).toBe(200);
    expect((response.headers()['cache-control'] || '').toLowerCase()).toContain('immutable');
  });

  test('security headers and a nonce-based CSP are present', async ({ page }) => {
    const response = await page.goto('/Account/Login');
    const headers = await response.headers();

    expect(headers['x-content-type-options']).toBe('nosniff');
    expect(headers['x-frame-options']).toBe('DENY');
    expect(headers['referrer-policy']).toBe('no-referrer');

    const csp = headers['content-security-policy'] || '';
    expect(csp).toContain("default-src 'self'");
    expect(csp).toContain("frame-ancestors 'none'");
    expect(csp).toContain("object-src 'none'");
    expect(csp).toContain("form-action 'self'");
    expect(csp).toMatch(/script-src 'self' 'nonce-([^']+)'/);

    // Browsers hide the nonce content attribute after parsing; the IDL property keeps it.
    const nonce = await page.locator('script:not([src])').first().evaluate((script) => script.nonce);
    expect(nonce).toBeTruthy();
    expect(csp).toContain(`'nonce-${nonce}'`);
  });

  test('redirects from protected pages carry no-store', async ({ page }) => {
    const response = await page.request.get('/Account', { maxRedirects: 0 });

    expect(response.status()).toBe(302);
    expect((response.headers()['cache-control'] || '').toLowerCase()).toContain('no-store');
    expect(response.headers()['location']).toContain('/Account/Login');
  });

  test('the signed-in landing page is not cached', async ({ page }) => {
    const { passwordUser } = require('../support/identities');
    const { signIn } = require('../support/flows');
    await signIn(page, passwordUser.email, passwordUser.password);

    const response = await page.goto('/');
    expect(response).not.toBeNull();
    expect(((await response.headers())['cache-control'] || '').toLowerCase()).toContain('no-store');
  });

  test('skip link is the first keyboard stop and points at main', async ({ page }) => {
    await page.goto('/Account/Login');

    await page.keyboard.press('Tab');
    const skip = page.locator('.skip-link');
    await expect(skip).toBeFocused();
    await expect(skip).toHaveAttribute('href', '#main');
  });

  test('empty password submission exposes aria-invalid and its error reference', async ({ page }) => {
    await page.goto('/Account/Login');
    await page.locator('#signin-btn').click();

    await expect(page.locator('#identifier')).toHaveAttribute('aria-invalid', 'true');
    await expect(page.locator('#identifier')).toHaveAttribute('aria-describedby', /identifier-error/);
    await expect(page.locator('#password')).toHaveAttribute('aria-invalid', 'true');
    await expect(page.locator('#password')).toHaveAttribute('aria-describedby', /password-error/);
  });

  test('the password form signs in without JavaScript', async ({ browser }) => {
    const context = await browser.newContext({ javaScriptEnabled: false, ignoreHTTPSErrors: true });
    const page = await context.newPage();
    const { passwordUser } = require('../support/identities');

    await page.goto('/Account/Login');
    await page.locator('#identifier').fill(passwordUser.email);
    await page.locator('#password').fill(passwordUser.password);
    await page.locator('#signin-btn').click();

    await expect(page).toHaveURL(/\/$/);
    await expect(page.locator('#email-done')).toHaveText(passwordUser.email);
    await context.close();
  });

  test('code inputs keep a 44px touch target at a 320px viewport', async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 800 });
    const { totpUser } = require('../support/identities');

    // Eight-character OTP group (an unknown-but-valid address keeps the shared test
    // identities free of resend cooldowns).
    await page.goto('/Account/Login');
    await page.locator('#email').fill(`layout-${Date.now()}@zaiware.dev`);
    await page.locator('#send-btn').click();
    await expect(page).toHaveURL(/\/Account\/EnterOtp/);

    for (const box of await page.locator('[data-code-digit]').evaluateAll((elements) =>
      elements.map((element) => element.getBoundingClientRect().toJSON()))) {
      expect(box.width).toBeGreaterThanOrEqual(44);
      expect(box.height).toBeGreaterThanOrEqual(44);
    }

    // Six-digit TOTP group.
    await page.goto('/Account/Login');
    await page.locator('#identifier').fill(totpUser.username);
    await page.locator('#password').fill(totpUser.password);
    await page.locator('#signin-btn').click();
    await expect(page).toHaveURL(/\/Account\/TwoFactor/);

    for (const box of await page.locator('[data-code-digit]').evaluateAll((elements) =>
      elements.map((element) => element.getBoundingClientRect().toJSON()))) {
      expect(box.width).toBeGreaterThanOrEqual(44);
      expect(box.height).toBeGreaterThanOrEqual(44);
    }
  });

  test('page CSS is scoped to its page', async ({ page }) => {
    await page.goto('/Account/Login');

    const scopedStyles = await page.locator('link[rel="stylesheet"]').evaluateAll((links) =>
      links.map((link) => link.getAttribute('href') || ''));

    expect(scopedStyles.some((href) => href.includes('.styles.css'))).toBe(true);
  });

  test('isolation scopes do not collide across pages', async ({ page }) => {
    await page.goto('/Account/Login');

    const html = await page.content();
    const loginScope = (html.match(/b-[a-z0-9]{10}/) || [])[0];
    expect(loginScope).toBeTruthy();

    const stylesHref = await page.locator('link[href*=".styles.css"]').getAttribute('href');
    const css = await (await page.request.get(new URL(stylesHref, page.url()).toString())).text();

    // Account's isolated rules are compiled under a different scope attribute than the
    // login page's, so a class-name collision cannot leak between the pages.
    const accountScope = (css.match(/\.account-avatar\[(b-[a-z0-9]{10})\]/) || [])[1];
    expect(accountScope).toBeTruthy();
    expect(accountScope).not.toBe(loginScope);
  });
});

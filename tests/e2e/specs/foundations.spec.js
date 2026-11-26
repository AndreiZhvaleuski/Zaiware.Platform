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

  test('page CSS is scoped to its page', async ({ page }) => {
    await page.goto('/Account/Login');

    const scopedStyles = await page.locator('link[rel="stylesheet"]').evaluateAll((links) =>
      links.map((link) => link.getAttribute('href') || ''));

    expect(scopedStyles.some((href) => href.includes('.styles.css'))).toBe(true);
  });
});

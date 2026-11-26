'use strict';

const { test, expect } = require('@playwright/test');
const { accountUser, passwordUser } = require('../support/identities');
const { signIn } = require('../support/flows');

const IDENTITY_BASE_URL = process.env.IDENTITY_BASE_URL || 'https://localhost:5001';
const PNG = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAFgwJ/lW+0WQAAAABJRU5ErkJggg==',
  'base64');

test.describe('avatars', () => {
  test('a user without an avatar sees the initials placeholder', async ({ page }) => {
    await signIn(page, passwordUser.email, passwordUser.password);
    await page.goto('/Account');

    const src = await page.locator('.account-avatar__image').getAttribute('src');
    const response = await page.request.get(new URL(src, IDENTITY_BASE_URL).toString());

    expect(response.status()).toBe(200);
    expect(response.headers()['content-type']).toContain('image/svg+xml');
    expect(await response.text()).toContain('<svg');
  });

  test('a valid upload is stored and served through the stable route', async ({ page }) => {
    await signIn(page, accountUser.email, accountUser.password);
    await page.goto('/Account');

    await page.locator('#Avatar').setInputFiles({
      name: 'avatar.png',
      mimeType: 'image/png',
      buffer: PNG,
    });
    await page.locator('#avatar-upload-btn').click();
    await expect(page.locator('.notice--success')).toContainText('Avatar updated');

    const src = await page.locator('.account-avatar__image').getAttribute('src');
    expect(src).toMatch(/^\/avatars\/[0-9a-f-]+/);

    const response = await page.request.get(new URL(src, IDENTITY_BASE_URL).toString());
    expect(response.status()).toBe(200);
    expect(response.headers()['content-type']).toContain('image/png');
  });

  test('rejects an oversized upload without changing the avatar', async ({ page }) => {
    await signIn(page, accountUser.email, accountUser.password);
    await page.goto('/Account');

    const before = await page.locator('.account-avatar__image').getAttribute('src');
    const beforeBytes = await (await page.request.get(new URL(before, IDENTITY_BASE_URL).toString())).body();

    await page.locator('#Avatar').setInputFiles({
      name: 'huge.png',
      mimeType: 'image/png',
      buffer: Buffer.concat([PNG, Buffer.alloc(2 * 1024 * 1024)]),
    });
    await page.locator('#avatar-upload-btn').click();

    await expect(page.locator('.notice--error')).toContainText('2 MB');

    const after = await page.locator('.account-avatar__image').getAttribute('src');
    expect(after.split('?')[0]).toBe(before.split('?')[0]);
    const afterBytes = await (await page.request.get(new URL(after, IDENTITY_BASE_URL).toString())).body();
    expect(afterBytes.equals(beforeBytes)).toBe(true);
  });

  test('rejects a disallowed content type', async ({ page }) => {
    await signIn(page, accountUser.email, accountUser.password);
    await page.goto('/Account');

    await page.locator('#Avatar').setInputFiles({
      name: 'notes.txt',
      mimeType: 'text/plain',
      buffer: Buffer.from('not an image'),
    });
    await page.locator('#avatar-upload-btn').click();

    await expect(page.locator('.notice--error')).toContainText('Choose a PNG, JPEG, or WebP image');
  });

  test('rejects a spoofed image content type', async ({ page }) => {
    await signIn(page, accountUser.email, accountUser.password);
    await page.goto('/Account');

    await page.locator('#Avatar').setInputFiles({
      name: 'fake.png',
      mimeType: 'image/png',
      buffer: Buffer.from('this is not a png'),
    });
    await page.locator('#avatar-upload-btn').click();

    await expect(page.locator('.notice--error')).toContainText('does not look like');
  });

  test('unauthenticated avatar and upload requests bounce to sign-in', async ({ browser, page }) => {
    await signIn(page, passwordUser.email, passwordUser.password);
    await page.goto('/Account');
    const src = await page.locator('.account-avatar__image').getAttribute('src');

    const anonymous = await browser.newContext({ ignoreHTTPSErrors: true });
    const request = anonymous.request;

    const avatar = await request.get(new URL(src, IDENTITY_BASE_URL).toString(), { maxRedirects: 0 });
    expect(avatar.status()).toBe(302);
    expect(avatar.headers()['location']).toContain('/Account/Login');

    const upload = await request.post(`${IDENTITY_BASE_URL}/Account?handler=Avatar`, {
      multipart: {
        Avatar: { name: 'a.png', mimeType: 'image/png', buffer: PNG },
      },
      maxRedirects: 0,
    });
    expect(upload.status()).toBe(302);
    expect(upload.headers()['location']).toContain('/Account/Login');

    const account = await request.get(`${IDENTITY_BASE_URL}/Account`, { maxRedirects: 0 });
    expect(account.status()).toBe(302);
    expect(account.headers()['location']).toContain('/Account/Login');

    await anonymous.close();
  });
});

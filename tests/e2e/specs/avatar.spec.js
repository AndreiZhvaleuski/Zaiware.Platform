'use strict';

const { test, expect } = require('@playwright/test');
const { accountUser, passwordUser } = require('../support/identities');
const { signIn } = require('../support/flows');
const {
  expectStatusBanner,
  expectStatusRemoved,
  submitAndReadLocation,
} = require('../support/status');

const IDENTITY_BASE_URL = process.env.IDENTITY_BASE_URL || 'https://localhost:5001';
// A genuinely valid 16x16 RGBA PNG (quadrant pattern) generated with zlib/CRC32 and
// verified by decoding it in Chromium. The previous 1x1 fixture had a bad IDAT CRC.
const PNG = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAAAIElEQVR42mOQ8b/xHx8mBBhGDRgWBhBSQMiCUQOGhQEAjpofLgaVi1oAAAAASUVORK5CYII=',
  'base64');

function watchForCspErrors(page) {
  const errors = [];
  page.on('console', (message) => {
    if (message.type() === 'error'
        && /content security policy|refused to load/i.test(message.text())) {
      errors.push(message.text());
    }
  });
  return errors;
}

async function expectImageDecoded(page) {
  await expect.poll(
    () => page.locator('.account-avatar__image').evaluate((image) =>
      image.complete && image.naturalWidth > 0 && image.naturalHeight > 0),
    { timeout: 15_000 },
  ).toBe(true);

  const size = await page.locator('.account-avatar__image').evaluate((image) => ({
    width: image.naturalWidth,
    height: image.naturalHeight,
  }));

  expect(size.width).toBeGreaterThan(0);
  expect(size.height).toBeGreaterThan(0);
}


test.describe('avatars', () => {
  test('a user without an avatar sees the initials placeholder', async ({ page }) => {
    await signIn(page, passwordUser.email, passwordUser.password);
    await page.goto('/Account');

    const src = await page.locator('.account-avatar__image').getAttribute('src');
    const response = await page.request.get(new URL(src, IDENTITY_BASE_URL).toString());

    expect(response.status()).toBe(200);
    expect(response.headers()['content-type']).toContain('image/svg+xml');
    // A same-second upload can reuse the cache-buster; the placeholder must never be stored.
    expect((response.headers()['cache-control'] || '').toLowerCase()).toContain('no-store');
    expect(await response.text()).toContain('<svg');
  });

  test('a valid upload is stored, served through the stable route, and rendered by the browser', async ({ page }) => {
    const cspErrors = watchForCspErrors(page);
    await signIn(page, accountUser.email, accountUser.password);
    await page.goto('/Account');

    await expect(page.locator('#Avatar')).toHaveAttribute('aria-invalid', 'false');
    await expect(page.locator('#Avatar')).toHaveAttribute('aria-describedby', 'avatar-hint');

    await page.locator('#Avatar').setInputFiles({
      name: 'avatar.png',
      mimeType: 'image/png',
      buffer: PNG,
    });

    const statusLocation = await submitAndReadLocation(
      page,
      () => page.locator('#avatar-upload-btn').click(),
      '/Account?handler=Avatar');

    expect(statusLocation).toContain('status=AvatarUpdated');
    await expectStatusBanner(page, 'Avatar updated.');
    await expectStatusRemoved(page);

    const src = await page.locator('.account-avatar__image').getAttribute('src');
    expect(src).toMatch(/^\/avatars\/[0-9a-f-]+/);
    const subjectId = src.split('?')[0].split('/').pop();

    const response = await page.request.get(new URL(src, IDENTITY_BASE_URL).toString());
    expect(response.status()).toBe(200);
    expect(response.headers()['content-type']).toContain('image/png');

    // The actual <img> must decode the presigned redirect target; page.request would bypass
    // CSP and hide a blocked image, so assert the DOM element's natural size instead.
    await expectImageDecoded(page);
    const decoded = await page.locator('.account-avatar__image').evaluate((image) => ({
      width: image.naturalWidth,
      height: image.naturalHeight,
    }));
    expect(decoded.width).toBe(16);
    expect(decoded.height).toBe(16);
    expect(cspErrors).toEqual([]);

    // The stable route redirects to a short-lived presigned URL for the subject's key.
    const redirect = await page.request.get(new URL(src, IDENTITY_BASE_URL).toString(), {
      maxRedirects: 0,
    });
    expect(redirect.status()).toBe(302);
    const location = redirect.headers()['location'] || '';
    expect(location).toContain(`/avatars/${subjectId}`);
    expect(location).toContain('X-Amz-Signature=');
    expect(location).toContain('X-Amz-Expires=300');
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

    // Profile fields must remain populated on the error render path.
    await expect(page.locator('#account-username')).toHaveValue(accountUser.username);
    await expect(page.locator('#account-display-name')).toHaveValue('E2E Account');

    const after = await page.locator('.account-avatar__image').getAttribute('src');
    expect(after.split('?')[0]).toBe(before.split('?')[0]);
    const afterBytes = await (await page.request.get(new URL(after, IDENTITY_BASE_URL).toString())).body();
    expect(afterBytes.equals(beforeBytes)).toBe(true);
  });

  test('rejects a body over the request limit before buffering the whole upload', async ({ page }) => {
    await signIn(page, accountUser.email, accountUser.password);
    await page.goto('/Account');

    const before = await page.locator('.account-avatar__image').getAttribute('src');
    const beforeBytes = await (await page.request.get(new URL(before, IDENTITY_BASE_URL).toString())).body();

    await page.locator('#Avatar').setInputFiles({
      name: 'too-large.png',
      mimeType: 'image/png',
      buffer: Buffer.concat([PNG, Buffer.alloc(3 * 1024 * 1024)]),
    });

    const responsePromise = page.waitForResponse((response) =>
      response.url().includes('/Account?handler=Avatar') && response.request().method() === 'POST');
    await page.locator('#avatar-upload-btn').click();
    const response = await responsePromise;

    expect([400, 413]).toContain(response.status());

    await page.goto('/Account');
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
    await expect(page.locator('#Avatar')).toHaveAttribute('aria-invalid', 'true');
    await expect(page.locator('#Avatar')).toHaveAttribute('aria-describedby', /avatar-hint/);
    await expect(page.locator('#Avatar')).toHaveAttribute('aria-describedby', /avatar-error/);
    await expect(page.locator('#avatar-error')).toContainText('does not look like');
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

'use strict';

const { expect } = require('@playwright/test');

function statusOf(page) {
  return new URL(page.url()).searchParams.get('status');
}

async function expectStatusRemoved(page) {
  await expect.poll(() => statusOf(page)).toBeNull();
}

async function expectNoStatusBanner(page) {
  await expect(page.locator('.notice--success')).toHaveCount(0);
}

async function expectStatusBanner(page, message) {
  const banner = page.locator('.notice--success');

  await expect(banner).toHaveText(message);
  await expect(banner).toHaveAttribute('role', 'status');
  await expect(banner).toBeVisible();
}

/**
 * Runs an action that triggers a navigation and returns the Location header of the first
 * matching response (followed or not). Use it to assert the status flag before the page
 * enhancement removes it from the address bar.
 */
async function submitAndReadLocation(page, action, urlPart, method = 'POST') {
  const responsePromise = page.waitForResponse((response) =>
    response.url().includes(urlPart) && response.request().method() === method);

  await action();
  const response = await responsePromise;
  return response.headers()['location'] || '';
}

module.exports = {
  expectNoStatusBanner,
  expectStatusBanner,
  expectStatusRemoved,
  statusOf,
  submitAndReadLocation,
};

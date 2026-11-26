'use strict';

const { defineConfig, devices } = require('@playwright/test');

const identityBaseUrl = process.env.IDENTITY_BASE_URL || 'https://localhost:5001';

module.exports = defineConfig({
  testDir: './specs',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 60_000,
  expect: {
    timeout: 10_000,
  },
  reporter: [['list']],
  use: {
    baseURL: identityBaseUrl,
    ignoreHTTPSErrors: true,
    headless: true,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
});

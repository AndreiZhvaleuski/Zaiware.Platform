'use strict';

const { defineConfig, devices } = require('@playwright/test');

const identityBaseUrl = process.env.IDENTITY_BASE_URL || 'https://localhost:5001';
const mailpitBaseUrl = process.env.MAILPIT_BASE_URL || 'http://localhost:8025';

// Tests read the same value from the environment; set the default once here.
process.env.MAILPIT_BASE_URL = mailpitBaseUrl;

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

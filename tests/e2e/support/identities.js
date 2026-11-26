'use strict';

module.exports = {
  passwordUser: {
    username: process.env.E2E_PASSWORD_USERNAME || 'e2e-password',
    email: process.env.E2E_PASSWORD_EMAIL || 'e2e-password@zaiware.dev',
    password: process.env.E2E_PASSWORD_PASSWORD || 'E2e-Password-2026!',
  },
  totpUser: {
    username: process.env.E2E_TOTP_USERNAME || 'e2e-totp',
    email: process.env.E2E_TOTP_EMAIL || 'e2e-totp@zaiware.dev',
    password: process.env.E2E_TOTP_PASSWORD || 'E2e-Totp-Password-2026!',
    secret: process.env.E2E_TOTP_SECRET || 'JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP',
  },
  demoUser: {
    username: process.env.E2E_DEMO_USERNAME || 'demo',
    email: process.env.E2E_DEMO_EMAIL || 'demo@zaiware.dev',
    password: process.env.E2E_DEMO_PASSWORD || 'Demo-Password-2026!',
  },
  accountUser: {
    username: process.env.E2E_ACCOUNT_USERNAME || 'e2e-account',
    email: process.env.E2E_ACCOUNT_EMAIL || 'e2e-account@zaiware.dev',
    password: process.env.E2E_ACCOUNT_PASSWORD || 'E2e-Account-Password-2026!',
  },
  securityUser: {
    username: process.env.E2E_SECURITY_USERNAME || 'e2e-security',
    email: process.env.E2E_SECURITY_EMAIL || 'e2e-security@zaiware.dev',
    password: process.env.E2E_SECURITY_PASSWORD || 'E2e-Security-Password-2026!',
  },
  passwordChangeUser: {
    username: process.env.E2E_PASSWORD_CHANGE_USERNAME || 'e2e-password-change',
    email: process.env.E2E_PASSWORD_CHANGE_EMAIL || 'e2e-password-change@zaiware.dev',
    password: process.env.E2E_PASSWORD_CHANGE_PASSWORD || 'E2e-Password-Change-2026!',
  },
  passwordSetUser: {
    username: process.env.E2E_PASSWORD_SET_USERNAME || 'e2e-password-set',
    email: process.env.E2E_PASSWORD_SET_EMAIL || 'e2e-password-set@zaiware.dev',
    // The first run sets a password through the account page; later runs on the same stack
    // exercise the change flow with this value instead.
    password: process.env.E2E_PASSWORD_SET_PASSWORD || 'E2e-Password-Set-2026!',
  },
};

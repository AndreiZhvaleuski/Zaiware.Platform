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
};

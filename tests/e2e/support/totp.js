'use strict';

const crypto = require('node:crypto');

const BASE32_ALPHABET = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';

function decodeBase32(secret) {
  const normalized = secret.replace(/=+$/, '').toUpperCase();
  const bytes = [];
  let bits = 0;
  let value = 0;

  for (const character of normalized) {
    const index = BASE32_ALPHABET.indexOf(character);

    if (index < 0) {
      throw new Error(`Invalid Base32 character: ${character}`);
    }

    value = (value << 5) | index;
    bits += 5;

    if (bits >= 8) {
      bits -= 8;
      bytes.push((value >>> bits) & 0xff);
      value &= (1 << bits) - 1;
    }
  }

  return Buffer.from(bytes);
}

/**
 * Computes an RFC 6238 TOTP code (SHA-1, 6 digits, 30-second step).
 * The server accepts the current step and one step either side.
 */
function totp(secret, offsetSteps = 0, now = Date.now()) {
  const step = Math.floor(now / 30_000) + offsetSteps;
  const counter = Buffer.alloc(8);
  counter.writeBigInt64BE(BigInt(step));

  const hmac = crypto.createHmac('sha1', decodeBase32(secret)).update(counter).digest();
  const offset = hmac[hmac.length - 1] & 0x0f;
  const binary = ((hmac[offset] & 0x7f) << 24)
    | (hmac[offset + 1] << 16)
    | (hmac[offset + 2] << 8)
    | hmac[offset + 3];

  return String(binary % 1_000_000).padStart(6, '0');
}

/**
 * Waits until the current 30-second window has enough life left for a submit, then
 * computes the code. This avoids the boundary race where the code is valid when typed
 * but stale when the server verifies it.
 */
async function freshTotp(secret, offsetSteps = 0) {
  const ageSeconds = (Date.now() % 30_000) / 1000;

  if (ageSeconds > 20) {
    await new Promise((resolve) => setTimeout(resolve, (30 - ageSeconds + 1) * 1000));
  }

  return totp(secret, offsetSteps);
}

/** Returns a six-digit code that is not in the server's current acceptance window. */
function wrongTotp(secret, now = Date.now()) {
  const accepted = new Set([
    totp(secret, -1, now),
    totp(secret, 0, now),
    totp(secret, 1, now),
  ]);

  for (let value = 0; value < 1_000_000; value += 1) {
    const candidate = String(value).padStart(6, '0');
    if (!accepted.has(candidate)) {
      return candidate;
    }
  }

  throw new Error('Could not construct a wrong TOTP code.');
}

module.exports = { totp, freshTotp, wrongTotp, decodeBase32 };

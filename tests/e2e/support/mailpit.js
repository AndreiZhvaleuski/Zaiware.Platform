'use strict';

const baseUrl = process.env.MAILPIT_BASE_URL || 'http://localhost:8025';

async function request(path, options) {
  const response = await fetch(`${baseUrl}${path}`, options);

  if (!response.ok) {
    throw new Error(`Mailpit request ${path} failed with ${response.status}: ${await response.text()}`);
  }

  return response;
}

async function clearMailbox() {
  await request('/api/v1/messages', { method: 'DELETE' });
}

async function latestMessageTo(email) {
  const response = await request('/api/v1/messages?limit=50');
  const body = await response.json();
  const normalized = email.toLowerCase();

  const match = (body.messages || []).find((message) =>
    (message.To || []).some((recipient) => (recipient.Address || '').toLowerCase() === normalized));

  if (!match) {
    throw new Error(`No Mailpit message addressed to ${email} was found.`);
  }

  const detail = await request(`/api/v1/message/${match.ID}`);
  return detail.json();
}

async function latestOtpTo(email) {
  const message = await latestMessageTo(email);
  const text = `${message.Subject || ''}\n${message.Text || ''}`;
  const match = text.match(/\b([A-Z0-9]{4})-([A-Z0-9]{4})\b/);

  if (!match) {
    throw new Error(`No one-time code was found in the Mailpit message for ${email}.`);
  }

  return `${match[1]}${match[2]}`;
}

module.exports = {
  baseUrl,
  clearMailbox,
  latestMessageTo,
  latestOtpTo,
};

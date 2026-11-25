(function () {
  'use strict';

  var CODE_LENGTH = 8;

  function $(id) { return document.getElementById(id); }

  var card = document.querySelector('.card');
  var status = $('status');

  function announce(msg) {
    if (!status) return;
    status.textContent = '';
    setTimeout(function () { status.textContent = msg; }, 30);
  }

  if (card && !window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
    card.addEventListener('pointermove', function (e) {
      var r = card.getBoundingClientRect();
      card.style.setProperty('--mx', ((e.clientX - r.left) / r.width * 100) + '%');
      card.style.setProperty('--my', ((e.clientY - r.top) / r.height * 100) + '%');
    });
  }

  function setBusy(btn, form, on) {
    if (btn) {
      btn.classList.toggle('is-loading', on);
      btn.disabled = on;
    }
    if (form) form.setAttribute('aria-busy', on ? 'true' : 'false');
    if (form) Array.prototype.forEach.call(form.querySelectorAll('input:not([type="hidden"])'), function (i) { i.disabled = on; });
  }

  /* ---------- email step ---------- */

  var emailForm = $('email-form');
  var emailInput = $('email');
  var emailError = $('email-error');
  var sendBtn = $('send-btn');
  var emailTouched = false;

  function setError(input, errorEl, msg) {
    if (errorEl) errorEl.textContent = msg || '';
    if (input) input.setAttribute('aria-invalid', msg ? 'true' : 'false');
  }

  function validEmail(v) { return /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/.test(v); }

  function emailMessage(v) {
    if (!v) return 'Enter your work email so we can send the code.';
    if (v.indexOf('@') === -1) return 'That address is missing an @ — try you@company.com.';
    if (!/\.[^\s@]{2,}$/.test(v)) return 'That address is missing a domain — try you@company.com.';
    return 'That address looks off — check for a typo, then try again.';
  }

  function validateEmail() {
    var v = emailInput.value.trim();
    if (validEmail(v)) { setError(emailInput, emailError, ''); return true; }
    setError(emailInput, emailError, emailMessage(v));
    return false;
  }

  if (emailForm && emailInput) {
    /* validate on first blur, then live-revalidate while invalid */
    emailInput.addEventListener('blur', function () {
      if (!emailInput.value.trim() && !emailTouched) { emailTouched = true; return; }
      emailTouched = true;
      validateEmail();
    });
    emailInput.addEventListener('input', function () {
      if (emailInput.getAttribute('aria-invalid') === 'true') validateEmail();
    });

    emailForm.addEventListener('submit', function (e) {
      if (!validateEmail()) {
        e.preventDefault();
        emailInput.focus();
        return;
      }
      setBusy(sendBtn, emailForm, true);
      announce('Sending your code.');
    });
  }

  /* ---------- code step ---------- */

  var otpForm = $('otp-form');
  var otpWrap = $('otp');
  var otpError = $('otp-error');
  var verifyBtn = $('verify-btn');
  var codeField = $('code');
  var inputs = otpWrap ? Array.prototype.slice.call(otpWrap.querySelectorAll('input')) : [];

  function code() {
    return inputs.map(function (i) { return i.value; }).join('');
  }

  function sanitize(value) {
    return String(value || '').replace(/[^A-Za-z0-9]/g, '').toUpperCase();
  }

  function clearInputError() {
    if (otpError) otpError.textContent = '';
    inputs.forEach(function (i) { i.removeAttribute('aria-invalid'); });
  }

  function showInputError(msg) {
    if (otpError) otpError.textContent = msg;
    inputs.forEach(function (i) { if (!i.value) i.setAttribute('aria-invalid', 'true'); });
    var firstEmpty = inputs.filter(function (i) { return !i.value; })[0];
    if (firstEmpty) firstEmpty.focus();
  }

  var autoTimer = null;

  function maybeSubmit() {
    clearTimeout(autoTimer);
    if (code().length !== CODE_LENGTH) return;
    if (!otpForm) return;
    autoTimer = setTimeout(function () {
      if (typeof otpForm.requestSubmit === 'function') otpForm.requestSubmit();
      else otpForm.submit();
    }, 220);
  }

  function fillFrom(startIndex, chars) {
    for (var k = 0; k < chars.length && startIndex + k < inputs.length; k++) {
      inputs[startIndex + k].value = chars[k];
    }
    inputs[Math.min(startIndex + chars.length, inputs.length - 1)].focus();
    clearInputError();
    maybeSubmit();
  }

  if (otpForm && otpWrap && inputs.length) {
    /* The visible positions are not bound; the combined value is what posts. */
    if (codeField) codeField.value = '';

    inputs.forEach(function (input, idx) {
      input.addEventListener('input', function () {
        var chars = sanitize(input.value);
        if (chars.length > 1) { fillFrom(idx, chars); return; }
        input.value = chars.slice(0, 1);
        if (input.value) {
          if (otpError && otpError.textContent) clearInputError();
          if (idx < inputs.length - 1) inputs[idx + 1].focus();
        }
        maybeSubmit();
      });

      input.addEventListener('keydown', function (e) {
        if (e.key === 'Backspace' && !input.value && idx > 0) {
          clearTimeout(autoTimer);
          inputs[idx - 1].focus();
          inputs[idx - 1].value = '';
          e.preventDefault();
        }
        if (e.key === 'ArrowLeft' && idx > 0) { inputs[idx - 1].focus(); e.preventDefault(); }
        if (e.key === 'ArrowRight' && idx < inputs.length - 1) { inputs[idx + 1].focus(); e.preventDefault(); }
      });

      input.addEventListener('paste', function (e) {
        var text = (e.clipboardData || window.clipboardData).getData('text') || '';
        var chars = sanitize(text);
        if (chars) {
          e.preventDefault();
          fillFrom(idx, chars.slice(0, inputs.length - idx));
        }
      });
    });

    otpForm.addEventListener('submit', function (e) {
      var value = code();
      if (value.length !== CODE_LENGTH) {
        e.preventDefault();
        showInputError('Enter all 8 characters from the email we sent.');
        announce('The code is incomplete.');
        return;
      }
      if (codeField) codeField.value = value;
      setBusy(verifyBtn, otpForm, true);
      announce('Checking your code.');
    });
  }

  /* ---------- resend countdown (server-driven) ---------- */

  var resendBtn = $('resend');
  var resendTimer = null;

  function initResendCountdown(btn) {
    var raw = btn.getAttribute('data-blocked-until');
    var until = raw ? Date.parse(raw) : NaN;

    if (!raw || isNaN(until)) {
      btn.disabled = false;
      btn.textContent = 'Resend code';
      return;
    }

    function tick() {
      var left = Math.ceil((until - Date.now()) / 1000);
      if (left <= 0) {
        clearInterval(resendTimer);
        btn.disabled = false;
        btn.textContent = 'Resend code';
        return;
      }
      btn.disabled = true;
      btn.textContent = 'Resend in ' + left + 's';
    }

    btn.disabled = true;
    clearInterval(resendTimer);
    resendTimer = setInterval(tick, 1000);
    tick();
  }

  if (resendBtn) {
    initResendCountdown(resendBtn);

    var resendForm = resendBtn.form;
    if (resendForm) {
      resendForm.addEventListener('submit', function () {
        clearInterval(resendTimer);
        resendBtn.disabled = true;
        resendBtn.textContent = 'Sending…';
        announce('Sending a new code.');
      });
    }
  }

  /* ---------- page load: focus the current step's heading ---------- */

  function ready(fn) {
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', fn);
    else fn();
  }

  ready(function () {
    var step = document.querySelector('.step');
    if (!step) return;

    var heading = step.querySelector('h1');
    if (heading) heading.focus();

    if (step.id === 'step-otp') {
      announce('Code sent. Enter the eight-character code from your email.');
    } else if (step.id === 'step-done') {
      var emailOut = $('email-done');
      announce("You're signed in" + (emailOut && emailOut.textContent ? ' as ' + emailOut.textContent : '') + '.');
    }

    var notice = document.querySelector('.notice');
    if (notice && notice.textContent.trim()) announce(notice.textContent.trim());

    if (step.id === 'step-email' && emailInput && emailError && emailError.textContent.trim()) {
      emailInput.focus();
    }
  });
})();

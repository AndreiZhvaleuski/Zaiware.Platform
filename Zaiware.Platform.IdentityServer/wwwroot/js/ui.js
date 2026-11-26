/*
 * Progressive enhancement only. Every form submits without this script.
 */
(function () {
  'use strict';

  function $(id) {
    return document.getElementById(id);
  }

  function all(selector, root) {
    return Array.prototype.slice.call((root || document).querySelectorAll(selector));
  }

  var status = $('status');

  function announce(message) {
    if (!status) {
      return;
    }

    status.textContent = '';
    window.setTimeout(function () {
      status.textContent = message;
    }, 30);
  }

  /* ---------- theme toggle ---------- */

  var STORAGE_KEY = 'theme';
  var toggle = $('theme-toggle');

  function effectiveTheme() {
    var override = document.documentElement.getAttribute('data-theme');
    if (override === 'light' || override === 'dark') {
      return override;
    }

    return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  }

  function syncToggleLabel() {
    if (!toggle) {
      return;
    }

    var next = effectiveTheme() === 'dark' ? 'light' : 'dark';
    toggle.setAttribute('aria-label', 'Switch to ' + next + ' theme');
    toggle.setAttribute('title', 'Switch to ' + next + ' theme');
  }

  if (toggle) {
    toggle.hidden = false;
    syncToggleLabel();

    toggle.addEventListener('click', function () {
      var next = effectiveTheme() === 'dark' ? 'light' : 'dark';
      document.documentElement.setAttribute('data-theme', next);

      try {
        localStorage.setItem(STORAGE_KEY, next);
      } catch (error) {
        /* The switch still works for this page when storage is blocked. */
      }

      syncToggleLabel();
    });
  }

  /* ---------- busy state ---------- */

  function setBusy(form, button, busy) {
    if (form) {
      form.setAttribute('aria-busy', busy ? 'true' : 'false');
      all('input, select, textarea', form).forEach(function (control) {
        if (control.type === 'hidden' || control.type === 'submit' || control.type === 'button') {
          return;
        }

        if (busy) {
          control.setAttribute('readonly', 'readonly');
        } else {
          control.removeAttribute('readonly');
        }
      });
    }

    if (button) {
      button.disabled = busy;
      button.classList.toggle('is-loading', busy);
    }
  }

  function resetBusyStates() {
    all('form[data-busy-form]').forEach(function (form) {
      setBusy(form, form.querySelector('[type="submit"]'), false);
    });
  }

  all('form[data-busy-form]').forEach(function (form) {
    form.addEventListener('submit', function () {
      if (form.checkValidity && !form.checkValidity()) {
        return;
      }

      setBusy(form, form.querySelector('[type="submit"]'), true);
      announce('Working…');
    });
  });

  window.addEventListener('pageshow', resetBusyStates);

  /* ---------- code input groups ---------- */

  all('[data-code-group]').forEach(function (group) {
    var form = group.closest('form');
    var hidden = form ? form.querySelector('[data-code-value]') : null;
    var inputs = all('input[data-code-digit]', group);
    var numeric = group.getAttribute('data-code-pattern') === 'numeric';
    var autoSubmit = group.getAttribute('data-auto-submit') !== 'false';
    var length = inputs.length;
    var timer = null;

    function sanitize(value) {
      var text = String(value || '');
      return numeric
        ? text.replace(/[^0-9]/g, '')
        : text.replace(/[^A-Za-z0-9]/g, '').toUpperCase();
    }

    function value() {
      return inputs.map(function (input) {
        return input.value;
      }).join('');
    }

    function sync() {
      if (hidden) {
        hidden.value = value();
      }
    }

    function maybeSubmit() {
      window.clearTimeout(timer);
      sync();

      if (!autoSubmit || !form || value().length !== length) {
        return;
      }

      timer = window.setTimeout(function () {
        if (typeof form.requestSubmit === 'function') {
          form.requestSubmit();
        } else {
          form.submit();
        }
      }, 220);
    }

    function fill(start, characters) {
      for (var index = 0; index < characters.length && start + index < length; index += 1) {
        inputs[start + index].value = characters[index];
      }

      var focusIndex = Math.min(start + characters.length, length - 1);
      inputs[focusIndex].focus();
      maybeSubmit();
    }

    inputs.forEach(function (input, index) {
      input.addEventListener('input', function () {
        var characters = sanitize(input.value);

        if (characters.length > 1) {
          fill(index, characters);
          return;
        }

        input.value = characters.slice(0, 1);
        if (input.value && index < length - 1) {
          inputs[index + 1].focus();
        }

        maybeSubmit();
      });

      input.addEventListener('keydown', function (event) {
        if (event.key === 'Backspace' && !input.value && index > 0) {
          window.clearTimeout(timer);
          inputs[index - 1].focus();
          inputs[index - 1].value = '';
          sync();
          event.preventDefault();
        }

        if (event.key === 'ArrowLeft' && index > 0) {
          inputs[index - 1].focus();
          event.preventDefault();
        }

        if (event.key === 'ArrowRight' && index < length - 1) {
          inputs[index + 1].focus();
          event.preventDefault();
        }
      });

      input.addEventListener('paste', function (event) {
        var text = (event.clipboardData || window.clipboardData).getData('text') || '';
        var characters = sanitize(text);

        if (characters) {
          event.preventDefault();
          fill(index, characters.slice(0, length - index));
        }
      });
    });

    if (form) {
      form.addEventListener('submit', function (event) {
        sync();

        if (value().length !== length) {
          event.preventDefault();
          var empty = inputs.filter(function (input) {
            return !input.value;
          })[0];

          if (empty) {
            empty.focus();
            empty.setAttribute('aria-invalid', 'true');
          }

          announce('The code is incomplete.');
        }
      });
    }
  });

  /* ---------- resend countdown (server-driven timestamp) ---------- */

  all('[data-blocked-until]').forEach(function (button) {
    var raw = button.getAttribute('data-blocked-until');
    var until = raw ? Date.parse(raw) : NaN;

    if (!raw || isNaN(until)) {
      return;
    }

    var defaultLabel = button.getAttribute('data-default-label') || button.textContent;
    var timer = window.setInterval(tick, 1000);

    function tick() {
      var left = Math.ceil((until - Date.now()) / 1000);

      if (left <= 0) {
        window.clearInterval(timer);
        button.disabled = false;
        button.textContent = defaultLabel;
        return;
      }

      button.disabled = true;
      button.textContent = 'Resend in ' + left + 's';
    }

    tick();
  });

  /* ---------- focus and announcements ---------- */

  function ready(callback) {
    if (document.readyState === 'loading') {
      document.addEventListener('DOMContentLoaded', callback);
    } else {
      callback();
    }
  }

  ready(function () {
    var heading = document.querySelector('[data-autofocus-heading]');
    if (heading) {
      heading.focus();
      window.setTimeout(function () {
        if (heading.textContent) {
          announce(heading.textContent.trim());
        }
      }, 60);
    }

    all('.notice[role="alert"], .notice--error, .notice--success').forEach(function (notice) {
      var text = notice.textContent.trim();
      if (text) {
        announce(text);
      }
    });
  });
})();

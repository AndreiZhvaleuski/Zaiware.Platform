# Spec Delta

## Purpose

Cross-cutting presentation requirements shared by every page of the identity provider's UI: dual-theme design tokens, per-page CSS isolation, cache correctness, an accessibility baseline, and progressive enhancement.

## ADDED Requirements

### Requirement: Design tokens with dual theme
All colors, spacing, radii, and motion values SHALL be defined as CSS custom properties in one token sheet; no raw color literals in page styles. The UI SHALL support light and dark themes, following the system preference by default with an optional manual override that persists. The theme SHALL load without a flash of the wrong theme, with a fallback when the dual-theme color function is unsupported.

#### Scenario: System preference drives the theme
- **WHEN** a user with a dark system preference and no stored override opens a page
- **THEN** the page renders in dark theme without a flash of light theme during load

#### Scenario: Manual override persists
- **WHEN** the user switches the theme manually and revisits later
- **THEN** the chosen theme is applied regardless of the system preference

### Requirement: Cache correctness for assets and auth pages
Static assets SHALL be served with content-fingerprinted URLs and immutable cache headers, and page stylesheets SHALL be referenced with content-hash versioning. Authentication pages SHALL be served with no-store cache directives so no signed-in state is recoverable from browser history.

#### Scenario: Assets are fingerprinted
- **WHEN** a stylesheet's content changes and the page is reloaded
- **THEN** the browser fetches the new content via a changed fingerprinted URL

#### Scenario: Auth pages are not cached
- **WHEN** a signed-in user navigates away from an account or sign-in page and navigates back
- **THEN** the browser refetches the page rather than restoring it from cache

### Requirement: Per-page CSS isolation
Page-specific styles SHALL live in per-page isolated stylesheets whose rules are scoped to that page. Styles shared across pages (tokens, buttons, fields, code-input groups, notices) SHALL live in a small shared sheet rather than being duplicated.

#### Scenario: Page styles are scoped
- **WHEN** two pages define conflicting rules for the same class name in their isolated stylesheets
- **THEN** each page renders with its own rule and neither leaks to the other

### Requirement: Accessible forms and state changes
Every page SHALL label its fields and associate validation messages with their inputs (aria-invalid / aria-describedby). State changes SHALL be announced politely, inputs SHALL be locked and the page marked busy during pending requests, focus SHALL move to the new heading on step changes, and a keyboard skip link SHALL be provided.

#### Scenario: Keyboard-only sign-in
- **WHEN** a user completes the sign-in flow using only the keyboard
- **THEN** every control is reachable, focus is visible, and state changes are announced

#### Scenario: Validation errors are associated
- **WHEN** a field fails validation
- **THEN** the input is marked invalid and its error message is programmatically associated with it

### Requirement: Motion, targets, and contrast
The UI SHALL respect reduced-motion preferences, provide interactive targets of at least 44 pixels, and meet AA contrast in both themes.

#### Scenario: Reduced motion is respected
- **WHEN** a user with a reduced-motion preference uses the UI
- **THEN** non-essential animations and transitions are suppressed

#### Scenario: Both themes meet AA contrast
- **WHEN** either theme is rendered
- **THEN** text and interactive elements meet AA contrast ratios against their backgrounds

### Requirement: Field hints for autofill and touch input
Sign-in fields SHALL carry appropriate autocomplete tokens (username, current-password, one-time-code) and code fields SHALL use numeric input modes on touch devices.

#### Scenario: Autofill recognizes the fields
- **WHEN** a password manager or mobile browser inspects the sign-in form
- **THEN** the username, password, and one-time-code fields expose their expected autocomplete tokens

### Requirement: Progressive enhancement
Every sign-in and account flow SHALL complete with JavaScript disabled: form submission and validation SHALL work server-side. Client scripts SHALL only enhance presentation (auto-advance code inputs, countdown timers, theme toggle) and SHALL load without a render-blocking framework dependency.

#### Scenario: Sign-in works without JavaScript
- **WHEN** a user with JavaScript disabled signs in with a password
- **THEN** the flow completes and the user reaches the protected page

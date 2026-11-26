# Spec Delta

## Purpose

Password-based sign-in on the combined sign-in screen: users authenticate with a username or email address plus a password, complete a TOTP challenge when two-factor authentication is enrolled, and may fall back to a recovery code. Includes the half-authenticated state that connects the two steps and the anti-enumeration error behavior.

## ADDED Requirements

### Requirement: Sign in with username and password
The system SHALL accept a username or an email address together with a password on the combined sign-in screen. The identifier SHALL be resolved as the profile's username first, falling back to the email attribute. Failed sign-ins SHALL produce a single generic error regardless of whether the identifier or the password was wrong.

#### Scenario: Correct username and password signs in
- **WHEN** a user submits their username with the correct password
- **THEN** the user is signed in and returned to the page that triggered login

#### Scenario: Correct email and password signs in
- **WHEN** a user submits their email address with the correct password
- **THEN** the user is signed in and returned to the page that triggered login

#### Scenario: Wrong credentials show a generic error
- **WHEN** a user submits an unknown identifier or a wrong password
- **THEN** the sign-in screen shows the same generic error message in both cases and no sign-in occurs

### Requirement: TOTP challenge for enrolled users
A user with an enrolled TOTP device SHALL be challenged for the current 6-digit authenticator code after their password is accepted and before a session is established. A user without an enrolled device SHALL be signed in directly after a correct password.

#### Scenario: Enrolled user completes the challenge
- **WHEN** a user with two-factor enabled signs in with a correct password and then submits a current authenticator code
- **THEN** the user is signed in and the session records multi-factor authentication

#### Scenario: Unenrolled user signs in directly
- **WHEN** a user without two-factor enabled signs in with a correct password
- **THEN** the user is signed in without any second-step prompt

#### Scenario: Wrong authenticator code is rejected
- **WHEN** a user submits an incorrect authenticator code at the challenge
- **THEN** the challenge reports the failure and no session is established

### Requirement: Recovery code fallback at the challenge
The TOTP challenge SHALL also accept a recovery code generated when two-factor authentication was enabled. Each recovery code SHALL be usable once.

#### Scenario: Recovery code completes the challenge
- **WHEN** a user submits a valid, unused recovery code at the challenge
- **THEN** the user is signed in and that recovery code cannot be used again

#### Scenario: Reused recovery code is rejected
- **WHEN** a user submits a recovery code that was already used
- **THEN** the challenge reports the failure and no session is established

### Requirement: Bounded half-authenticated state
The system SHALL carry a short-lived, encrypted half-authenticated state between password acceptance and second-factor completion. Reaching the challenge step without passing the password step SHALL return the user to the sign-in screen, and the state SHALL NOT itself grant any authenticated access.

#### Scenario: Direct navigation to the challenge bounces to login
- **WHEN** a user opens the challenge page without a preceding successful password step
- **THEN** the user is redirected to the sign-in screen

# Spec Delta

## Purpose

Passwordless sign-in for the platform's identity provider: invited users receive a one-time code by email, enter it, and get a sign-in session. Covers code delivery, invite-only enforcement, resend and attempt limits, session establishment, and sign-out.

## ADDED Requirements

### Requirement: Request a one-time code by email
The system SHALL accept an email address on the login page and, when the address is syntactically valid, dispatch a one-time code to it by email. Codes SHALL be 8-character alphanumeric strings and expire 5 minutes after issue.

#### Scenario: Valid email requests a code
- **WHEN** a user submits a valid email address on the login page
- **THEN** the system sends an email containing a one-time code to that address and advances the user to the code-entry step

#### Scenario: Malformed email is rejected before sending
- **WHEN** a user submits a value that is not a valid email address
- **THEN** the system shows a validation error explaining the problem and sends no code

### Requirement: Invite-only sign-in
The system SHALL allow sign-in only for pre-registered users. Verification of a code sent to an email address with no existing user record SHALL fail and SHALL NOT create a user account.

#### Scenario: Unregistered email cannot sign in
- **WHEN** a user enters a correct code for an email address that has no user record
- **THEN** sign-in fails with an error and no user account is created

#### Scenario: Registered email signs in
- **WHEN** a user enters a correct, unexpired code for an email address that has a user record
- **THEN** sign-in succeeds and the user's existing identity is used

### Requirement: Resend with server-driven cooldown
The code-entry step SHALL offer a resend action that issues a new code, invalidating the previous one. The system SHALL refuse a resend within 60 seconds of the last issued code and the login UI SHALL show the remaining wait time rather than a fixed countdown.

#### Scenario: Resend after cooldown
- **WHEN** the user requests a new code more than 60 seconds after the last one was issued
- **THEN** a new code is emailed and the previous code no longer grants sign-in

#### Scenario: Resend during cooldown
- **WHEN** the user requests a new code within 60 seconds of the last issue
- **THEN** no code is sent and the UI indicates how long the user must wait

### Requirement: Limited verification attempts
The system SHALL reject the fifth and subsequent verification attempts for a given code even if the code is correct, requiring a new code to be requested.

#### Scenario: Too many wrong codes
- **WHEN** a user enters a wrong code four times and then enters the correct code
- **THEN** verification is rejected and the user is told to request a new code

### Requirement: Session establishment and return to origin
Successful verification SHALL establish the identity provider's sign-in session and return the user to the page that triggered the login. A user with an active session SHALL NOT be asked to sign in again while the session is valid.

#### Scenario: Unauthenticated visit bounces through login and back
- **WHEN** an unauthenticated user requests a protected page, completes email and code entry
- **THEN** the user is signed in and lands back on the originally requested page

#### Scenario: Signed-in user skips login
- **WHEN** a user with a valid session opens the login page
- **THEN** the user proceeds to the protected content without re-entering a code

### Requirement: Code-entry UI behavior
The code-entry UI SHALL present separate input positions matching the code length, accept a pasted code into those positions, auto-submit when all positions are filled, and provide a way back to the email step. The login UI SHALL announce state changes accessibly (validation errors, pending requests, step changes) and keep inputs locked while a request is pending.

#### Scenario: Paste a code
- **WHEN** the user pastes a full code into the first input position
- **THEN** all positions fill and the code is submitted automatically

#### Scenario: Change email mid-flow
- **WHEN** the user chooses to go back to the email step after a code was sent
- **THEN** the email step is shown again with the previous address editable

### Requirement: Sign-out
The system SHALL end the sign-in session in response to an explicit sign-out action and then require a new code to sign in again.

#### Scenario: Sign out ends the session
- **WHEN** a signed-in user activates sign-out
- **THEN** the session is ended and the next visit to a protected page redirects to the login page

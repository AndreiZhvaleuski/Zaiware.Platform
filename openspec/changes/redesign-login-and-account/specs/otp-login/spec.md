# Spec Delta

## MODIFIED Requirements

### Requirement: Request a one-time code by email
The system SHALL offer a one-time-code action on the combined sign-in screen. The action SHALL accept an email address and, when the address is syntactically valid, dispatch a one-time code to it by email. Codes SHALL be 8-character alphanumeric strings and expire 5 minutes after issue.

#### Scenario: Valid email requests a code
- **WHEN** a user submits a valid email address through the one-time-code action on the sign-in screen
- **THEN** the system sends an email containing a one-time code to that address and advances the user to the code-entry step

#### Scenario: Malformed email is rejected before sending
- **WHEN** a user submits a value that is not a valid email address through the one-time-code action
- **THEN** the system shows a validation error explaining the problem and sends no code

#### Scenario: Sign-in screen offers the passwordless path
- **WHEN** an unauthenticated user opens the sign-in screen
- **THEN** both the password sign-in form and the one-time-code action are available without page navigation

### Requirement: Code-entry UI behavior
The code-entry UI SHALL present separate input positions matching the code length, accept a pasted code into those positions, auto-submit when all positions are filled, and provide a way back to the sign-in screen. The login UI SHALL announce state changes accessibly (validation errors, pending requests, step changes) and keep inputs locked while a request is pending, following the shared presentation foundations.

#### Scenario: Paste a code
- **WHEN** the user pastes a full code into the first input position
- **THEN** all positions fill and the code is submitted automatically

#### Scenario: Change email mid-flow
- **WHEN** the user chooses to go back to the sign-in screen after a code was sent
- **THEN** the combined sign-in screen is shown again with the previous address editable

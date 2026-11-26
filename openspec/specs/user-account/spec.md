# user-account Specification

## Purpose

The signed-in account area: profile management (unique username, display name, read-only email, avatar with preview) and security management (password set/change, TOTP enrollment and disable, recovery codes). Available only to authenticated users.

## Requirements

### Requirement: Authenticated account area
The system SHALL present an account area only to authenticated users. The area SHALL contain a profile section and a security section.

#### Scenario: Unauthenticated access is refused
- **WHEN** an unauthenticated user requests the account area
- **THEN** the user is redirected to the sign-in screen

### Requirement: Set a unique username
The account area SHALL let the signed-in user set their username, enforcing uniqueness across all users. A taken or invalid username SHALL be rejected with an actionable error; a successful change SHALL make the new username usable for password sign-in.

#### Scenario: Username is set and usable for sign-in
- **WHEN** the user saves a unique username and then signs out and back in with it
- **THEN** the password sign-in with the new username succeeds

#### Scenario: Duplicate username is rejected
- **WHEN** the user saves a username that another user already has
- **THEN** the account area shows an error and the username is unchanged

### Requirement: Edit display name
The account area SHALL let the signed-in user edit their display name, persisted to the profile and reflected after save.

#### Scenario: Display name change persists
- **WHEN** the user saves a new display name and reloads the account area
- **THEN** the new display name is shown

### Requirement: Email is displayed read-only
The account area SHALL display the user's email address without offering to change it; the email remains the OTP delivery address and identity anchor.

#### Scenario: Email shown without edit affordance
- **WHEN** the user views the profile section
- **THEN** the email address is displayed and no email edit control is offered

### Requirement: Avatar upload with validation
The account area SHALL let the signed-in user upload an avatar image. Uploads SHALL be restricted to allowed image types and a maximum size; invalid uploads SHALL be rejected with a specific error. A successful upload SHALL immediately update the avatar preview.

#### Scenario: Valid avatar upload updates the preview
- **WHEN** the user uploads an image of an allowed type and size
- **THEN** the avatar preview shows the new image

#### Scenario: Oversized or wrong-type upload is rejected
- **WHEN** the user uploads a file exceeding the size limit or of a disallowed type
- **THEN** the account area shows a specific error and the stored avatar is unchanged

### Requirement: Set or change password
The signed-in user SHALL be able to set a password when none exists, or change it by confirming the current password. Violations of the password policy SHALL be reported with actionable errors; a changed password SHALL take effect on the next sign-in.

#### Scenario: Password change takes effect
- **WHEN** the user changes their password by confirming the current one and then signs out
- **THEN** sign-in with the new password succeeds and the old password fails

#### Scenario: Weak password is rejected
- **WHEN** the user submits a new password that violates the configured policy
- **THEN** the account area shows which policy the password violates and the password is unchanged

### Requirement: TOTP enrollment with recovery codes
The account area SHALL offer enabling two-factor authentication: present a QR code and manual key for authenticator apps, require a current code to confirm, then generate and display recovery codes exactly once. Until the confirming code is accepted, two-factor authentication SHALL NOT be enabled.

#### Scenario: Enrollment completes and takes effect
- **WHEN** the user scans the QR code, submits a current authenticator code, and the recovery codes are displayed
- **THEN** the next password sign-in presents the TOTP challenge

#### Scenario: Enrollment abandoned before confirmation
- **WHEN** the user starts enrollment but never submits a confirming code
- **THEN** two-factor authentication remains disabled and no TOTP challenge appears at sign-in

### Requirement: TOTP disable and recovery-code regeneration
The account area SHALL let the user disable two-factor authentication by removing enrolled devices, and regenerate recovery codes, invalidating all previous codes. The remaining recovery-code count SHALL be visible.

#### Scenario: Disabling two-factor removes the challenge
- **WHEN** the user disables two-factor authentication and signs in with a password
- **THEN** no TOTP challenge is presented

#### Scenario: Regenerated codes invalidate old ones
- **WHEN** the user regenerates recovery codes and then tries an older code at the challenge
- **THEN** the old code is rejected and a new code works

# Spec Delta

## Purpose

Defines how the Aspire sandbox kit prepares the environment the AppHost and its diagnostics expect: a compatible CLI, the DCP and transport settings, and a trusted per-sandbox HTTPS development certificate.

## ADDED Requirements

### Requirement: Sandbox provides certificate-store tooling

A sandbox created from the Aspire kit SHALL have a `certutil` implementation available so `aspire doctor` can verify and update NSS certificate stores. The kit SHALL install it at create time from the base distribution's package manager and SHALL grant only the package repository hosts that install needs.

#### Scenario: Fresh sandbox has certutil

- **WHEN** a sandbox is created from the Aspire kit
- **THEN** `certutil` resolves on `PATH`
- **AND** `aspire doctor` does not report `certutil is not available`

#### Scenario: Install phase is narrowly granted

- **WHEN** the Aspire kit's network policy is inspected
- **THEN** the install-phase allow list contains only the Debian package host(s) needed for the package install
- **AND** the runtime allow list still covers only the container registries the stack pulls from

### Requirement: Per-sandbox trusted development certificate

Each boot of the sandbox SHALL ensure a trusted, exportable ASP.NET Core HTTPS development certificate exists for the agent user. If none is trusted, the kit SHALL create and trust one; if a trusted certificate already exists, it SHALL be reused. No certificate private key SHALL be baked into the kit image.

#### Scenario: First boot creates the certificate

- **WHEN** a freshly created sandbox boots
- **THEN** a per-sandbox HTTPS development certificate is created and trusted
- **AND** `aspire doctor` reports the certificate as trusted
- **AND** the DCP developer-certificate connection check succeeds

#### Scenario: Later boots reuse the certificate

- **WHEN** the sandbox restarts with an already trusted certificate
- **THEN** the startup step leaves the certificate in place
- **AND** no duplicate development certificates are created

### Requirement: OpenSSL clients discover the trust directory

The sandbox SHALL export `SSL_CERT_DIR` so OpenSSL-based clients load the per-user ASP.NET Core certificate trust directory together with the system CA directories.

#### Scenario: Certificate is usable outside the CLI

- **WHEN** an OpenSSL-based client runs in the sandbox environment
- **THEN** `SSL_CERT_DIR` includes both the system certificate directory and `~/.aspnet/dev-certs/trust`
- **AND** `aspire doctor` does not report partial trust or an unconfigured `SSL_CERT_DIR`

### Requirement: Sandbox documentation matches the provisioned state

Aspire kit documentation and agent context SHALL describe the provisioned certificate and tooling, and MUST NOT claim that the image has no trusted certificate or that the doctor certificate warnings are expected.

#### Scenario: Guidance reflects the kit

- **WHEN** the Aspire agent context and the sandbox README Aspire section are inspected
- **THEN** they state that a trusted development certificate is provisioned per sandbox
- **AND** they no longer list missing certificate/`certutil` warnings as expected
- **AND** they still document the `http` launch profile and loopback-only publishing

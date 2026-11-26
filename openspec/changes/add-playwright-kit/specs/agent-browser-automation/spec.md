# Spec Delta

## Purpose

Gives the sandboxed agent a reproducible browser-automation toolchain so it can render pages, inspect browser behavior, capture screenshots and traces, and run Playwright tests without leaving the sandbox.

## ADDED Requirements

### Requirement: Sandbox includes a pinned Playwright toolchain

The sandbox environment SHALL include the published, verified `sbx/playwright` mixin at an exact release, and SHALL expose the `playwright` CLI and `@playwright/test` to the agent user. The kit reference MUST NOT use a floating `latest` tag.

#### Scenario: Fresh sandbox exposes Playwright

- **WHEN** the sandbox is created from the committed environment file and creation completes
- **THEN** `playwright --version` succeeds for the agent user and reports the pinned release
- **AND** a Node process can resolve `@playwright/test` without a project-local install

#### Scenario: Environment file pins the release

- **WHEN** the `kits:` list in `sbxenv.yaml` is inspected
- **THEN** the Playwright mixin uses an exact version tag or digest
- **AND** no `latest` tag is used for it

### Requirement: Chromium runs headlessly for the agent user

The mixin SHALL provide Chromium and its headless shell at a shared browser path writable by the agent, and the sandbox SHALL retain the mixin's `PLAYWRIGHT_BROWSERS_PATH` setting. Browser automation MUST work without a display server.

#### Scenario: Headless browser launch and screenshot

- **WHEN** the agent runs a Playwright screenshot command against a page served from localhost inside the sandbox
- **THEN** the command exits successfully and writes a non-empty image file

#### Scenario: Shared browser path is usable

- **WHEN** the agent inspects the Playwright environment and browser directory
- **THEN** `PLAYWRIGHT_BROWSERS_PATH` points at the shared path
- **AND** the agent user can read and write that directory

#### Scenario: Headed-only features are not available

- **WHEN** there is no display server and the agent requests headed mode, UI mode, or codegen
- **THEN** the command fails rather than pretending that a window is available
- **AND** headless screenshots, PDFs, traces, and videos remain supported

### Requirement: Playwright guidance reaches the agent

The mixin's agent context SHALL be staged under `/usr/share/sandbox/kit/playwright/` and indexed by the generated global `AGENTS.md`. The guidance MUST tell the agent that Playwright and `@playwright/test` are global, the browser path must be preserved, only headless Chromium is provisioned, and external sites need policy grants.

#### Scenario: Context is staged and indexed after recreation

- **WHEN** the sandbox is created with the Playwright kit
- **THEN** `/usr/share/sandbox/kit/playwright/playwright-context.md` exists and is non-empty
- **AND** the generated `/home/agent/.config/opencode/AGENTS.md` indexes the `playwright` kit context

#### Scenario: Guidance states the operating limits

- **WHEN** the staged Playwright context is read
- **THEN** it identifies the global packages, the shared browser path, the headless-only constraint, and the need for separate grants for external sites

### Requirement: Network access is scoped to installation and browser assets

The Playwright kit SHALL allow the package repositories needed to install Chromium/native dependencies during sandbox creation and the browser-binaries CDN at runtime. It MUST NOT imply access to arbitrary sites under test.

#### Scenario: Creation installs Playwright dependencies

- **WHEN** the sandbox is created from a clean host state
- **THEN** the Playwright install hook completes without a network-policy denial for its package repositories

#### Scenario: Localhost works and unapproved external sites do not

- **WHEN** a Playwright script visits a page served from localhost inside the sandbox
- **THEN** the page loads
- **AND** when it visits an external site that has no sandbox policy grant, the request fails with a policy/proxy error rather than bypassing the policy

### Requirement: Existing sandbox behavior is preserved

Adding the Playwright kit SHALL NOT change the OpenCode server startup, authenticated port, clone workspace, persistent session volume, or the context staged by existing kits.

#### Scenario: Server and workspace still work after recreation

- **WHEN** the recreated sandbox has finished booting
- **THEN** authenticated `http://127.0.0.1:4096/openapi.json` returns `200` and anonymous access returns `401`
- **AND** the workspace is still the private clone and existing kit context files remain staged and indexed

### Requirement: Kit changes take effect through recreation

Documentation SHALL state that adding or changing the Playwright kit requires a full sandbox recreation, and that changing the pinned release is an explicit upgrade followed by verification.

#### Scenario: Plan and recreate apply the new kit

- **WHEN** the environment file changes and the operator runs `sbx env plan`, then removes and recreates the sandbox
- **THEN** the plan shows the Playwright kit change and the new sandbox uses the pinned release
- **AND** `sbx env exec` alone is not documented as a way to apply the kit change

# Spec Delta

## Purpose

Defines which vendor skill packs and specialist subagents an OpenCode session in this repository exposes, how each is sourced and pinned, and how pack components that OpenCode cannot host are handled.

## ADDED Requirements

### Requirement: Duende identity skills are available

Sessions SHALL expose the Duende Agent Skills set from the pinned Duende revision, covering IdentityServer hosting, configuration, UI flows, resilience, storage, key management, API protection, token lifecycle, user management, testing, BFF, OAuth/OIDC protocols, and ASP.NET Core authentication and authorization.

#### Scenario: Identity work discovers a relevant skill

- **WHEN** a session asks how to configure an IdentityServer client, refresh token rotation, or an external login provider
- **THEN** the corresponding Duende skill is advertised and its full instructions can be loaded by ID

#### Scenario: A loaded skill carries its supporting files

- **WHEN** a Duende skill that references supporting documents is loaded
- **THEN** the referenced files resolve inside that skill's own directory

### Requirement: Identity specialist subagents are available

Sessions SHALL expose the Duende `identity-server-specialist` and `oauth-oidc-specialist` roles as subagents that can be launched for focused diagnosis or protocol review.

#### Scenario: Launching the IdentityServer specialist

- **WHEN** the agent launches the `identity-server-specialist` subagent with an IdentityServer question
- **THEN** the subagent runs with the Duende specialist instructions and returns its findings to the parent session

#### Scenario: Launching the OAuth/OIDC specialist

- **WHEN** the agent launches the `oauth-oidc-specialist` subagent with a protocol or security question
- **THEN** the subagent runs with the OAuth/OIDC specialist instructions and returns its findings to the parent session

### Requirement: Curated .NET skills are available

Sessions SHALL expose the skills from the `dotnet`, `dotnet-aspnetcore`, `dotnet-diag`, and `dotnet-test` plugins of the .NET team skill repository. The `dotnet-msbuild` and `dotnet-upgrade` plugins SHALL be excluded as redundant with the core `msbuild`, `csharp-refactoring`, and `dotnet-breaking-changes` skills, and no other unrelated plugins SHALL be installed.

#### Scenario: Build and test work discovers a .NET skill

- **WHEN** a session investigates an MSBuild failure, an xUnit/Microsoft.Testing.Platform test run, an ASP.NET Core endpoint, or a C# refactoring/breaking-change review
- **THEN** at least one skill from the selected .NET plugins is advertised as relevant and can be loaded by ID

#### Scenario: Unrelated .NET plugins are absent

- **WHEN** the available skills are listed
- **THEN** no skill from the `dotnet-msbuild` or `dotnet-upgrade` plugins, or from the Blazor, MAUI, WinForms, AI, experimental, template-engine, NuGet, or test-migration plugins, is advertised

### Requirement: Aspire skills load from the official pinned catalog

Sessions SHALL load the eight official Aspire skills through Aspire's OpenCode V2 HTTP catalog, pinned to an immutable revision so the skill content does not change without a repository change.

#### Scenario: Aspire work discovers a skill

- **WHEN** a session performs Aspire AppHost, orchestration, monitoring, deployment, initialization, or terminal work
- **THEN** the corresponding Aspire skill is advertised and can be loaded

#### Scenario: Catalog content is stable for a given commit

- **WHEN** the same repository revision is used on two different days
- **THEN** the Aspire catalog URL resolves to the same pinned revision and serves the same skill versions

### Requirement: Skill and agent sources are pinned and documented

Every installed skill pack and subagent SHALL record its source repository, pinned commit, included components, and update procedure in a committed document next to the skills.

#### Scenario: A maintainer can identify provenance

- **WHEN** a maintainer opens the skill provenance document
- **THEN** each installed pack names its upstream repository and immutable commit, and lists what was included or intentionally omitted

#### Scenario: The catalog source is documented like a vendored pack

- **WHEN** a maintainer inspects the Aspire entry
- **THEN** the document records its upstream repository, pinned commit, and catalog URL alongside the vendored packs

#### Scenario: Updating a pack is deliberate

- **WHEN** a pack is updated
- **THEN** the pinned revision in the provenance document changes in the same commit as the vendored content or catalog URL

### Requirement: Skill packs can be refreshed reproducibly

The repository SHALL provide one documented, scripted update path that refreshes the vendored packs, re-pins the Aspire catalog, and can check whether the working tree matches the recorded pins.

#### Scenario: Drift check reports the current state

- **WHEN** a maintainer runs the update script in check mode
- **THEN** it reports whether vendored content and the Aspire catalog URL match the recorded pins, and exits non-zero on drift

#### Scenario: Update produces a reviewable change

- **WHEN** a maintainer runs the update script against a newer upstream revision
- **THEN** the script rewrites the vendored packs and pins, and the resulting diff can be reviewed before committing

#### Scenario: The update path is discoverable to the agent

- **WHEN** the user asks the agent to update the installed skill packs
- **THEN** a dedicated skill explains how to run the script, review the diff, and verify the result

### Requirement: Unsupported host-specific pack components are excluded

The installed setup SHALL NOT depend on Claude, Copilot, Cursor, or Codex plugin marketplaces, Copilot `.agent.md` custom agents, Copilot canvas extensions, or other host-specific packaging.

#### Scenario: Session starts with only portable components

- **WHEN** an OpenCode session starts at the repository root
- **THEN** it loads skills, subagents, and MCP configuration without requiring a plugin marketplace or an unsupported file format

#### Scenario: Unsupported components are documented, not half-installed

- **WHEN** a pack ships a component OpenCode cannot host
- **THEN** the component is absent from the repository and the provenance document records the omission and its reason

### Requirement: Skill discovery needs no per-session setup

Installed skills SHALL be discovered from committed project configuration and files alone, without a manual copy, install command, or configuration edit in each new session or sandbox.

#### Scenario: Fresh clone advertises the same skills

- **WHEN** the repository is checked out in a sandbox created from this repository's kit configuration
- **THEN** the Duende, .NET, and Aspire skills are advertised without any setup step beyond starting OpenCode

### Requirement: Existing OpenSpec skills remain available

Installing the new packs SHALL NOT remove, override, or rename the OpenSpec workflow skills already used by this repository.

#### Scenario: OpenSpec workflows still load

- **WHEN** the user invokes an OpenSpec propose, apply, or archive workflow
- **THEN** the corresponding OpenSpec skill loads with its existing instructions

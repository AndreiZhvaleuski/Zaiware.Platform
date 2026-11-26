# Spec Delta

## Purpose

Defines how the local Aspire AppHost is configured, built, and started so the identity server stack stays reproducible and free of obsolete project-SDK diagnostics.

## ADDED Requirements

### Requirement: AppHost build has no obsolete-SDK diagnostic

A build of the solution SHALL succeed without the obsolete AppHost SDK diagnostic (`ASPIRE012`) or any equivalent obsolete-project-SDK warning. The AppHost project SHALL use a supported SDK configuration with explicitly declared Aspire hosting and .NET project integration packages.

#### Scenario: Solution build is clean of SDK diagnostics

- **WHEN** a developer runs `dotnet build` at the repository root with the pinned .NET SDK
- **THEN** the build succeeds
- **AND** the output contains no `ASPIRE012` or other obsolete AppHost SDK warning

#### Scenario: AppHost packages are explicit and centrally pinned

- **WHEN** the AppHost project and central package file are inspected
- **THEN** `Aspire.Hosting.AppHost` and `Aspire.Hosting.Dotnet` are explicit package references
- **AND** their versions are pinned in `Directory.Packages.props` at the same Aspire release
- **AND** the project no longer sets an `Aspire.AppHost.Sdk` SDK attribute

### Requirement: Project resources are declared by path

The AppHost SHALL declare the identity server as a path-based project resource and SHALL NOT depend on a compile-time project reference to promote it into the application model. Building the AppHost alone MUST NOT be the mechanism that builds the service.

#### Scenario: Identity server is a path-based resource

- **WHEN** the AppHost source is inspected
- **THEN** the identity server is added with `AddDotnetProject` and its project path
- **AND** the AppHost project contains no resource `ProjectReference` to the identity server

#### Scenario: AppHost-only build does not build the service

- **WHEN** only the AppHost project is built
- **THEN** the build succeeds without building the identity server project
- **AND** the solution build still includes and builds the identity server

#### Scenario: Orchestrated run builds the resource project

- **WHEN** the AppHost is started with the Aspire CLI
- **THEN** a project-build resource appears in the application model for the identity server
- **AND** the identity server starts from the path declared in the AppHost

### Requirement: Local stack starts from the Aspire CLI bundle

The AppHost SHALL obtain the dashboard and DCP from the installed Aspire CLI bundle instead of RID-specific dashboard/DCP package references, and the CLI version SHALL be compatible with the AppHost package version.

#### Scenario: Stack starts and becomes healthy

- **WHEN** a developer starts the AppHost with the `http` launch profile through the Aspire CLI
- **THEN** PostgreSQL, the identity server database, Mailpit, RustFS, the avatars bucket, and the identity server all report running
- **AND** the identity server reports healthy at its configured endpoint

#### Scenario: Stack can be inspected and stopped

- **WHEN** the running stack is inspected with `aspire describe`
- **THEN** the resource names, types, and endpoints match the existing local stack
- **AND** `aspire stop` stops the AppHost cleanly

#### Scenario: Missing or mismatched CLI is diagnosed

- **WHEN** the AppHost is built or started on a machine without a compatible Aspire CLI bundle
- **THEN** the tooling reports the bundle resolution failure instead of silently omitting the dashboard or DCP

### Requirement: Aspire version anchors stay coherent

Sandbox documentation and kit guidance SHALL track the Aspire CLI and AppHost package versions together, and MUST NOT describe the obsolete SDK attribute as the version anchor.

#### Scenario: Sandbox docs match the AppHost configuration

- **WHEN** the sandbox Aspire kit documentation is inspected
- **THEN** it anchors the CLI version to the Aspire package versions used by the AppHost
- **AND** it contains no instruction to bump an `Aspire.AppHost.Sdk` attribute

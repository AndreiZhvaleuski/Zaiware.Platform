# Spec Delta

## Purpose

Gives OpenCode sessions in this repository C# and Razor language-server diagnostics so compiler errors and warnings reach the agent loop without a VS Code extension or a manual build step.

## ADDED Requirements

### Requirement: Roslyn language server is configured for C# and Razor

Project configuration SHALL register the .NET Roslyn language server for `.cs`, `.razor`, and `.cshtml` files, launched through the repository's existing .NET SDK tooling.

#### Scenario: Opening a C# file starts the Roslyn server

- **WHEN** a session opens or edits a `.cs` file in the repository
- **THEN** OpenCode starts the configured Roslyn language server and associates it with that file

#### Scenario: Razor pages get the same server

- **WHEN** a session opens or edits a `.cshtml` or `.razor` file
- **THEN** the same Roslyn language server handles the file instead of a separate Razor extension

### Requirement: Only one C# language server handles a file

The setup SHALL avoid running a second C# language server for the same file, so diagnostics are not duplicated or contradictory.

#### Scenario: Built-in C# server does not duplicate Roslyn

- **WHEN** the configured language servers are inspected for a `.cs` file
- **THEN** exactly one server handles that extension and it is the configured Roslyn server

### Requirement: Missing diagnostics do not block a session

If the Roslyn server cannot start, sessions SHALL continue to load skills, subagents, and tools normally and SHALL report the failure without preventing work.

#### Scenario: Language server unavailable

- **WHEN** the Roslyn server cannot be started, for example because its package cannot be restored
- **THEN** the session still starts, skills remain available, and the agent can fall back to `dotnet build` diagnostics

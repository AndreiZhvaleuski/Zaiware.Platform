# Spec Delta

## Purpose

Defines which MCP servers OpenCode exposes for development sessions in this repository, and the toolchain, persistence, and network conditions they require to keep working in kit-created sandboxes and across sandbox recreations.

## ADDED Requirements

### Requirement: NuGet MCP server is available in project sessions
OpenCode sessions started in this repository SHALL expose the official NuGet MCP server over stdio, with no authentication, providing tools for vulnerability remediation planning, latest-version lookup, package documentation context, package version update planning, and supply-chain security review.

#### Scenario: Session startup connects the NuGet server
- **WHEN** an OpenCode session starts at the repository root
- **THEN** the `nuget` MCP server reports connected and its tools are callable without any sign-in

#### Scenario: Version lookup answers from the package source
- **WHEN** the agent asks for the latest available version of a published package
- **THEN** the NuGet server's lookup tool returns a version published on the configured package source

### Requirement: Duende documentation MCP server is available in project sessions
OpenCode sessions started in this repository SHALL expose the Duende Documentation MCP server, providing tools for full-text search and page fetches over Duende documentation, blog posts, and IdentityServer sample code.

#### Scenario: Session startup connects the Duende server
- **WHEN** an OpenCode session starts at the repository root
- **THEN** the `duende-mcp` MCP server reports connected and its search and fetch tools are callable

#### Scenario: Documentation search returns current content
- **WHEN** the agent searches the indexed Duende content for a documented concept
- **THEN** the server returns results sourced from the live Duende documentation, blog, or samples index

### Requirement: MCP commands are portable across kit-created sandboxes
The committed project configuration SHALL launch both MCP servers through the `dnx` package runner by name, without machine-specific absolute paths, so the same configuration works in any sandbox created from this repository's kit configuration whose .NET 10 SDK provides `dnx` on PATH.

#### Scenario: Clone in a kit-created sandbox connects without config edits
- **WHEN** the repository is used in a sandbox created from this repository's kit configuration with the .NET 10 SDK installed and `dnx` resolvable on PATH
- **THEN** both MCP servers connect from the committed configuration with no local modifications

### Requirement: The sandbox toolchain exposes dnx on PATH
The sandbox toolchain defined in this repository SHALL expose `dnx` on PATH in every shell and process spawn, durably across sandbox recreation.

#### Scenario: Fresh shell resolves dnx
- **WHEN** a new shell, login or non-login, starts in the sandbox
- **THEN** `dnx` resolves on PATH and `dnx --help` shows the tool runner usage

#### Scenario: Recreated sandbox still exposes dnx
- **WHEN** the sandbox is destroyed and recreated from the repository's kit configuration
- **THEN** `dnx` resolves on PATH without any manual repair step

### Requirement: The Duende index persists outside the repository
The Duende MCP server's SQLite index SHALL be stored under OpenCode's persistent data directory, outside the repository working tree, and SHALL be retained across sandbox recreation.

#### Scenario: Index lands in the persistent data directory
- **WHEN** the Duende MCP server starts
- **THEN** its index database is created under OpenCode's persistent data directory and no database file appears in the repository

#### Scenario: Index survives sandbox recreation
- **WHEN** the sandbox is destroyed and recreated
- **THEN** the Duende index database is reattached from persistent storage and the server refreshes it incrementally instead of rebuilding from scratch

### Requirement: Duende indexing endpoints are allowed egress
The sandbox network policy SHALL allow HTTPS access to the Duende documentation site, the Duende blog, and the GitHub endpoints used to download sample code.

#### Scenario: Indexing completes without firewall denial
- **WHEN** the Duende MCP server refreshes its documentation, blog, and samples indexes
- **THEN** all indexing requests complete without being blocked by the sandbox network policy

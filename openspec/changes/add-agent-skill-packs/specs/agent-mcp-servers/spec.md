# Spec Delta

## ADDED Requirements

### Requirement: Microsoft Learn MCP server is available in project sessions

OpenCode sessions started in this repository SHALL expose the free Microsoft Learn MCP server over its public remote endpoint, with no authentication, providing tools for official Microsoft documentation search, page fetch, and code-sample search.

#### Scenario: Session startup connects the Microsoft Learn server

- **WHEN** an OpenCode session starts at the repository root
- **THEN** the `microsoft-learn` MCP server reports connected and its documentation search and fetch tools are callable without any sign-in

#### Scenario: Documentation lookup answers from Microsoft Learn

- **WHEN** the agent asks for current .NET, ASP.NET Core, or Microsoft platform guidance
- **THEN** the Microsoft Learn tools return content sourced from official Microsoft documentation rather than model memory alone

### Requirement: Microsoft Learn endpoint is allowed egress

The sandbox network policy SHALL allow HTTPS access to `learn.microsoft.com` so the remote MCP server can answer from inside a kit-created sandbox.

#### Scenario: Microsoft Learn calls complete without firewall denial

- **WHEN** the agent invokes a Microsoft Learn MCP tool in a sandbox created from this repository's kit configuration
- **THEN** the request completes without being blocked by the sandbox network policy

## MODIFIED Requirements

### Requirement: MCP commands are portable across kit-created sandboxes
The committed project configuration SHALL address every configured MCP server through a portable command or public endpoint, without machine-specific absolute paths, so the same configuration works in any sandbox created from this repository's kit configuration. Stdio servers launched through `dnx` SHALL use the package name, and the remote Microsoft Learn server SHALL use its public HTTPS endpoint.

#### Scenario: Clone in a kit-created sandbox connects without config edits
- **WHEN** the repository is used in a sandbox created from this repository's kit configuration with the .NET 10 SDK installed and `dnx` resolvable on PATH
- **THEN** all configured MCP servers connect from the committed configuration with no local modifications

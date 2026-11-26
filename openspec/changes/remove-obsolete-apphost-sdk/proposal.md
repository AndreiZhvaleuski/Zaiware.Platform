# Proposal

## Why

`Zaiware.Platform.AppHost` still opts into `Aspire.AppHost.Sdk/17.0.0`, which Aspire 17 marks obsolete and reports as `ASPIRE012` on every build. The previous change deliberately staged the migration and accepted the warning; the AppHost already calls `AddDotnetProject`, so the remaining SDK-only behaviors (implicit resource promotion of `ProjectReference`s and implicit dashboard/DCP package references) are no longer needed. Removing the obsolete SDK now leaves the solution warning-free and avoids the SDK's eventual removal becoming a forced migration.

## What Changes

- Switch the AppHost project from `Aspire.AppHost.Sdk/17.0.0` to `Microsoft.NET.Sdk`.
- Add an explicit central version and `PackageReference` for `Aspire.Hosting.AppHost` (17.0.0) alongside the existing `Aspire.Hosting.Dotnet` reference; keep `AspireUseCliBundle=true` so the dashboard and DCP continue to come from the installed Aspire CLI.
- Drop the AppHost's `ProjectReference` to `Zaiware.Platform.IdentityServer`. The resource is already declared by path with `AddDotnetProject`, and Project Resource v2 coordinates its build when the AppHost runs.
- Update sandbox documentation/comments that name `Aspire.AppHost.Sdk` as the version anchor, so the Aspire CLI pin is tracked against the AppHost package versions instead.
- No change to orchestrated resources, endpoints, environment variables, launch profiles, or the identity server itself.

## Capabilities

### New Capabilities

- `aspire-apphost`: how the AppHost project is configured and built (SDK, centrally pinned Aspire packages, CLI bundle, path-based project resources) and how the local stack is started for development.

### Modified Capabilities

<!-- None: no existing product behavior changes. -->

## Impact

- `Zaiware.Platform.AppHost/Zaiware.Platform.AppHost.csproj`: SDK attribute, package references, removed resource `ProjectReference`.
- `Directory.Packages.props`: new `Aspire.Hosting.AppHost` package version.
- `sbx/README.md` and `sbx/kits/aspire/aspire.yaml` comment: replace the obsolete SDK version anchor with the Aspire package versions.
- Build behavior: `dotnet build` on the solution stays green and loses `ASPIRE012`; building the AppHost alone no longer builds the identity server (the solution and CI still do).
- Tooling requirement: the Aspire CLI 17.0.0 must remain installed wherever `aspire run`/`aspire start` is used.
- Historical note: the archived `redesign-login-and-account` decision that accepted `ASPIRE012` is superseded by this change; archived artifacts are left untouched.

# Design

## Context

See `proposal.md` — Why. Relevant current state:

- `Zaiware.Platform.AppHost.csproj` uses `Sdk="Aspire.AppHost.Sdk/17.0.0"`, keeps `<AspireUseCliBundle>true</AspireUseCliBundle>`, references `Aspire.Hosting.Dotnet`, PostgreSQL, MailPit, and RustFs, and carries a `ProjectReference` to `Zaiware.Platform.IdentityServer`.
- `AppHost.cs` already declares the identity server through `AddDotnetProject("identity-server", "../Zaiware.Platform.IdentityServer/Zaiware.Platform.IdentityServer.csproj", options => options.LaunchProfileName = "SelfHost")`; no generated `Projects.*` metadata is used anywhere.
- Versions are centrally managed in `Directory.Packages.props` (Aspire packages at 17.0.0), and the sandbox Aspire CLI is 17.0.0.
- The migration path is documented in the Aspire repository's `docs/migrate-from-apphost-sdk.md` and linked from the `Aspire.Hosting.AppHost` package README.

A scratch copy of the repository was migrated and exercised during planning: the solution built with 0 warnings, and `aspire start --launch-profile http` brought up PostgreSQL, the identity server database, Mailpit, RustFS, the avatars bucket, and a healthy identity server; `aspire stop` shut it down cleanly.

## Goals / Non-Goals

**Goals:**

- Remove the obsolete AppHost SDK opt-in and the `ASPIRE012` diagnostic without changing the orchestrated stack.
- Keep one explicit, centrally pinned Aspire package set that the sandbox CLI version can track.
- Prove the path-based project resource still builds and starts through Project Resource v2.

**Non-Goals:**

- Upgrading Aspire, .NET, Duende, or any other package beyond adding the missing AppHost package.
- Changing resource names, endpoints, environment variables, launch profiles, or identity server behavior.
- Rewriting archived planning artifacts that documented the staged migration.

## Decisions

### Use `Microsoft.NET.Sdk` with explicit `Aspire.Hosting.AppHost`

Replace the SDK attribute with `Microsoft.NET.Sdk` and add `<PackageReference Include="Aspire.Hosting.AppHost" />`, versioned centrally at 17.0.0 next to `Aspire.Hosting.Dotnet`. The package supplies `IsAspireHost`, the AppHost targets, and the `dotnet run` integration; `Aspire.Hosting.Dotnet` continues to supply `AddDotnetProject`.

Alternatives considered:

- **Suppress `ASPIRE012`** (`SuppressAspireAppHostSdkObsoleteWarning`/`NoWarn`): removes the symptom but keeps the deprecated opt-in and its implicit behaviors; rejected because the migration is small and already proven.
- **Keep the SDK for staged migration**: no benefit once `AddDotnetProject` is the only resource declaration style.

### Keep `AspireUseCliBundle=true` explicitly

The package defaults this to true for C# AppHosts, but keeping the property makes the CLI-bundle dependency visible and preserves the current behavior: the dashboard and DCP come from the installed Aspire CLI rather than RID-specific package references.

Alternative considered: omit the property and rely on the default. Rejected only because the explicit property documents the requirement in the project file; behavior is identical.

### Remove the AppHost's `ProjectReference` to the identity server

With the plain SDK, a `ProjectReference` would become an ordinary assembly reference and the AppHost would copy the service output; it would also imply the resource is built whenever the AppHost builds. Project Resource v2 already builds the service from the `AddDotnetProject` path at run time, and the service stays in `Zaiware.Platform.slnx`, so solution and CI builds still cover it. No typed helpers (`WithMigrationsProject<Projects.*>`), Blazor metadata, or test fixtures use the generated metadata here.

Alternative considered: keep the `ProjectReference` with `IsAspireProjectResource="false"`. Rejected as dead weight that changes AppHost build ordering and output without adding value.

### Keep launch configuration and resource wiring untouched

`Properties/launchSettings.json`, `appsettings*.json`, `aspire.config.json`, ports, volumes, environment variables, waits, and the `SelfHost` launch profile already work with the CLI bundle and are unrelated to the SDK identity. Changing them would widen the blast radius without addressing the warning.

### Re-anchor sandbox documentation to package versions

`sbx/README.md` and the `sbx/kits/aspire/aspire.yaml` header comment currently say the CLI version must track the `Aspire.AppHost.Sdk` pin in the AppHost project. Change that wording to track the Aspire package versions in `Directory.Packages.props` and the AppHost package references. The kit's actual pinned version (17.0.0) already matches.

## Risks / Trade-offs

- **AppHost-only build no longer builds the identity server** → the solution remains the build entry point and still contains the service; document the behavior in the Change and keep CI building the solution.
- **A machine without the Aspire CLI can no longer build the AppHost** → this is already the case with `AspireUseCliBundle=true`; the diagnostics (`ASPIRE009`/`ASPIRE013`) explain the fix, and the kit installs a matching CLI.
- **Future typed-resource features may need the generated `Projects.*` metadata** → none are used today; if one is introduced, add a specific `ProjectReference` with the required metadata instead of restoring the obsolete SDK.
- **Transient resource health during startup** (PostgreSQL and the identity server briefly report unhealthy/starting) → not a regression; re-check `aspire describe` before treating a run as failed.
- **Verification was done in the Linux sandbox with CLI 17.0.0** → repeat build and run verification on the Windows host, which is the original failing environment, before archiving.

## Migration Plan

1. Apply the project and package-file edits, then the documentation wording update, in one commit.
2. Restore/build the solution and confirm zero warnings, including no `ASPIRE012`.
3. Start the stack through the Aspire CLI with the `http` profile, confirm all resources become healthy, then stop it.
4. Rollback: restore `Sdk="Aspire.AppHost.Sdk/17.0.0"`, remove the `Aspire.Hosting.AppHost` reference/version, and re-add the `ProjectReference`.

## Open Questions

None.

# Tasks

## 1. Migrate the AppHost project

- [x] 1.1 In `Zaiware.Platform.AppHost/Zaiware.Platform.AppHost.csproj`, change the project SDK to `Microsoft.NET.Sdk`, add `<PackageReference Include="Aspire.Hosting.AppHost" />`, keep `AspireUseCliBundle=true`, and remove the `ProjectReference` to the identity server; verify the file has no `Aspire.AppHost.Sdk` attribute and `AppHost.cs` still declares the service through `AddDotnetProject`.
- [x] 1.2 Add `<PackageVersion Include="Aspire.Hosting.AppHost" Version="17.0.0" />` to `Directory.Packages.props` next to the other Aspire 17.0.0 versions; verify all Aspire `PackageVersion` entries report the same version.
- [x] 1.3 Run `dotnet build Zaiware.Platform.slnx`; verify the build succeeds with 0 warnings, no `ASPIRE012`, and that the output lists the AppHost, identity server, service defaults, and tests as built.
- [x] 1.4 Run `dotnet build Zaiware.Platform.AppHost/Zaiware.Platform.AppHost.csproj`; verify it succeeds and its output does not include the identity server project build.

## 2. Align sandbox documentation

- [x] 2.1 Update the Aspire section of `sbx/README.md` so the CLI version requirement points at the Aspire package versions in `Directory.Packages.props` and the AppHost package references instead of `Aspire.AppHost.Sdk`; verify the section still states the required CLI version and no longer names the SDK attribute.
- [x] 2.2 Update the header comment in `sbx/kits/aspire/aspire.yaml` to track the AppHost package versions instead of the AppHost SDK; verify the kit default `aspireVersion` (17.0.0), the Dockerfile `ASPIRE_VERSION` default, and the package versions all agree.
- [x] 2.3 Search the live project files (`sbx/`, `Zaiware.Platform.AppHost/`, `Directory.Packages.props`, excluding `openspec/changes/archive/`, `bin/`, and `obj/`) for `Aspire.AppHost.Sdk`; verify no live reference remains.

## 3. Verify the running stack

- [x] 3.1 Run `aspire doctor` and confirm the installed CLI is 17.0.0 or newer and compatible with `Aspire.Hosting.AppHost` 17.0.0.
- [x] 3.2 Start the stack with `aspire start --non-interactive --launch-profile http`; verify `aspire describe` eventually reports PostgreSQL, the IdentityServer database, Mailpit, RustFS, the avatars bucket, and the identity server as running, and the identity server as healthy with its configured endpoint.
- [x] 3.3 Exercise the identity server over its endpoint (for example the status/health page) to confirm the path-based resource runs with the same database, SMTP, avatars, and data-protection configuration as before.
- [x] 3.4 Run `aspire stop`; verify the AppHost stops cleanly and no AppHost remains in `aspire ps`.

## Workflow follow-up

- Confirm `dotnet build` is warning-free on the Windows host used to report the warning.
- Archive the change after review.

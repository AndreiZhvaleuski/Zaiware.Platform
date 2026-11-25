# Aspire

- The Aspire CLI is preinstalled. `DOTNET_ROOT`, IPv4 DCP, HTTP transport, and telemetry opt-out are already configured.
- Use the `http` launch profile; the image has no trusted ASP.NET Core development certificate.
- Do not override `NO_PROXY` or add `[::1]`.
- From the workspace root, start in the background with `aspire start --non-interactive --launch-profile http`.
- Inspect with `aspire ps`, `aspire describe`, or `aspire logs <resource>`; stop with `aspire stop`.

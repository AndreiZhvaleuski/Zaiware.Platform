# Aspire

- The Aspire CLI is preinstalled (17.0.0). `DOTNET_ROOT`, IPv4 DCP, HTTP transport, telemetry opt-out, and Docker Hub/MCR image-pull access are already configured.
- Use the `http` launch profile for the AppHost; the image has no trusted ASP.NET Core development certificate for the dashboard. The IdentityServer resource declares the `SelfHost` HTTPS profile explicitly because the `__Host-SecondFactorState` cookie requires HTTPS.
- Do not override `NO_PROXY` or add `[::1]`.
- From the workspace root, start in the background with `aspire start --non-interactive --launch-profile http`.
- Inspect with `aspire ps`, `aspire describe`, or `aspire logs <resource>`; stop with `aspire stop`.

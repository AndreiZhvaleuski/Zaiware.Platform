# Aspire

- The Aspire CLI is preinstalled (17.0.0). `DOTNET_ROOT`, IPv4 DCP, HTTP transport, telemetry opt-out, Docker Hub/MCR image-pull access, and a trusted per-sandbox HTTPS development certificate (`certutil` included) are already configured; `aspire doctor` reports no certificate warnings.
- Use the `http` launch profile for the AppHost: the dashboard stays inside the sandbox (loopback-only publishing), so HTTP keeps host access simple. The IdentityServer resource declares the `SelfHost` HTTPS profile explicitly because the `__Host-SecondFactorState` cookie requires HTTPS.
- The certificate lives in `~/.aspnet/dev-certs` and `~/.dotnet`; the kit recreates trust on boot if it is missing. Do not delete those directories while the stack runs.
- Do not override `SSL_CERT_DIR`, `NO_PROXY`, or add `[::1]`.
- From the workspace root, start in the background with `aspire start --non-interactive --launch-profile http`.
- Inspect with `aspire ps`, `aspire describe`, or `aspire logs <resource>`; stop with `aspire stop`.

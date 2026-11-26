# Tasks

## 1. Provision certificate tooling and trust in the kit

- [x] 1.1 In `sbx/kits/aspire/aspire.yaml`, add an `install.allow` list containing `deb.debian.org` to the `network-policy@1` capability while keeping the existing `runtime.allow` registries unchanged.
- [x] 1.2 In the same file, add a `lifecycle@1` capability with an install hook (`user: "0"`, proxy env) that runs `apt-get update`, installs `libnss3-tools` with `--no-install-recommends`, and clears the apt lists; and a startup hook (`user: agent`) that runs `aspire certs trust --non-interactive` only while `dotnet dev-certs https --check --trust` fails.
- [x] 1.3 Add a kit argument that exports `SSL_CERT_DIR` as `/usr/lib/ssl/certs:/home/agent/.aspnet/dev-certs/trust`, and update the kit header comment to describe the install/startup certificate work.

## 2. Align documentation and agent context

- [x] 2.1 Update `sbx/kits/aspire/aspire-context.md` so it states that a trusted per-sandbox certificate and `certutil` are provisioned, keeps the `http` launch-profile guidance, and removes the "no trusted certificate" claim.
- [x] 2.2 Update the Aspire section of `sbx/README.md`: document the install/startup hooks, the install-phase network grant, `SSL_CERT_DIR`, and replace the "doctor certificate warnings are expected" paragraph with the expected clean result.

## 3. Verify

- [x] 3.1 Verify in the current sandbox that the hook commands work as written: `certutil` resolves, a trusted certificate exists for the agent user, and `aspire doctor` reports 6 passed, 0 warnings, 0 failed.
- [x] 3.2 Verify the trust step is safe on a clean home: with a temporary `HOME` and matching `SSL_CERT_DIR`, `aspire certs trust --non-interactive` creates exactly one trusted certificate and `dotnet dev-certs https --check --trust` succeeds as the non-root agent user.
- [x] 3.3 Validate the kit descriptor parses and the changed YAML is well formed.
- [ ] 3.4 After retrieving the change on the host, run `sbx env plan`, then `sbx env rm` and `sbx env create`, and confirm `sbx env exec -- bash -lc 'cd "$WORKSPACE_DIR" && aspire doctor'` reports 0 warnings and no duplicate certificates.

## Workflow follow-up

- Add an evidence-log row to `sbx/MIXINS.md` only after 3.4 passes.
- Confirm the AppHost stack still starts and the identity server is healthy after the recreate.

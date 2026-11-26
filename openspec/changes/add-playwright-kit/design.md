# Design

## Context

See `proposal.md` for motivation. Current state and constraints:

- `sbxenv.yaml` selects the `docker.io/sbx/shell:1.0.0` workload and five local mixins: `provider-keys`, `opencode-server`, `dotnet`, `aspire`, and `openspec`. The repo targets sbx v0.50.0 and sandbox-kit schema v3; `opencode-server` owns the global `AGENTS.md`, and the other local kits contribute body-only `agent-context@1` files.
- The workspace uses clone mode, and OpenCode state is a persistent `volume@1`. Kit changes are create-time only: `sbx env exec` and attaching do not apply them.
- The project has no Node.js project or Playwright tests today. The browser toolchain is for the agent, not for an application build step.
- Docker publishes a verified `sbx/playwright` mixin. Docker's environment-file documentation uses `docker.io/sbx/playwright:1.61.1` as its worked example. The published 1.61.1 descriptor is schema v3, kind `mixin`, provides `playwright@1.61.1`, and declares `network-policy@1`, `lifecycle@1`, and `agent-context@1`. Its install hook adds Node 24 only when Node is absent and runs `playwright install-deps chromium`; it declares no `requires` and no kit arguments.
- The same repository also publishes `1.65.1-0` (currently also `latest`). That descriptor adds `requires: [deb/apt, deb/bash, deb/coreutils, deb/grep]` and a required `version` kit argument. `sbx/playwright-kit` is the older Docker Hub repository built from the schema-v2 `main` source; it is not the current official recommendation.
- The 1.61.1 kit image is a single ~292 MB layer. It carries the Playwright CLI/test runner and browser assets; the create-time hook adds Node when needed and installs Chromium's native system libraries. The browser directory is `/opt/ms-playwright`, writable by the agent; `NODE_PATH` makes the global packages resolvable without a project-local `node_modules`.

## Goals / Non-Goals

**Goals:**

- Add the supported Playwright mixin with a reproducible, non-floating pin.
- Keep the change to one kit entry plus documentation and verification.
- Preserve clone mode, the authenticated OpenCode server, the persistent volume, and every existing kit's behavior.
- Give future operators a clear upgrade and recreation procedure.

**Non-Goals:**

- Writing a local Playwright kit or copying the published layer into this repo.
- Registering a Playwright MCP server or changing OpenCode MCP configuration.
- Adding a project Playwright test project, test fixtures, or CI workflow.
- Supporting headed mode, UI mode, codegen, Firefox, or WebKit.
- Upgrading to the `1.65.1-0` packaging in this change; it has a different argument and `requires` contract and must be probed separately.

## Decisions

### D1 — Use the published verified mixin, not a local kit

Add this reference to the end of the `kits:` list in `sbxenv.yaml`:

```yaml
  # Published, verified Docker kit: Playwright CLI, @playwright/test, and
  # headless Chromium. Pinned; see sbx/README.md before changing the tag.
  - docker.io/sbx/playwright:1.61.1
```

Rationale: Docker documents this as the supported way to add Playwright to an environment file, and the verified publisher maintains the browser/system-library contract. A local kit would duplicate a 292 MB layer, own the Chromium apt dependencies, and drift from the upstream fixes. Alternatives rejected: `sbx/playwright-kit` (older repository/source, not the current docs path); a custom Playwright MCP kit (different capability, not requested); a host-side browser (breaks sandbox isolation and the clone workflow).

### D2 — Pin `1.61.1`, not `latest` or `1.65.1-0`

`1.61.1` is the exact version used by Docker's environment-file example, has no required kit arguments, and declares no `requires`. The repo's `MIXINS.md` records that `requires` is valid v3 but remains unverified here, so consuming a kit that depends on four `deb/*` provides is an unnecessary creation risk for this change. `latest` is a moving target and its current descriptor needs `--kit-arg version=...`; pinning it would make sandbox creation non-reproducible.

If a future upgrade to `1.65.1-0` is wanted, use the object form with the matching argument and probe the `deb/*` requirements with a separate sandbox first:

```yaml
  - source: docker.io/sbx/playwright:1.65.1-0
    args:
      version: "1.65.1"
```

### D3 — Let the kit own environment and capabilities

Do not add a local install hook, a `/usr/local/bin` symlink, `PLAYWRIGHT_BROWSERS_PATH`, `NODE_PATH`, `NO_PROXY`, or a duplicate network policy. The published kit already provides all of them, and a second kit writing the same file path or environment variable would collide or silently override the kit's values. `sbxenv.yaml` only selects the kit.

### D4 — Document the published-kit pattern in `MIXINS.md`

The local authoring playbook currently assumes every tool is built under `sbx/kits/`. Add a short note: a verified published mixin is preferred when it covers the tool; pin a version tag or digest; inspect its capabilities/`requires` before adopting it; verify it against the workload; and do not create a local duplicate. The evidence log gets an entry only after a real create and verification pass.

### D5 — Verify through a full recreate, not `exec`

The acceptance check is `sbx env plan` (shows the kit addition and policy domains), `sbx env rm` after retrieving any sandbox-only commits, and `sbx env create`. Then verify:

- `playwright --version` and a Node resolution of `@playwright/test`;
- `PLAYWRIGHT_BROWSERS_PATH` and writability of `/opt/ms-playwright`;
- a headless screenshot of a local `file://` page writes a non-empty PNG;
- `/usr/share/sandbox/kit/playwright/playwright-context.md` is staged and indexed in `AGENTS.md`;
- authenticated `/openapi.json` is `200`, anonymous is `401`, and other kit contexts are still present;
- `sbx policy ls` / the create plan shows the Playwright install/runtime domains.

`exec` against an existing sandbox is explicitly insufficient because it skips kit composition and lifecycle install.

## Risks / Trade-offs

- **First creation is slower and larger** — the mixin adds a ~292 MB layer and runs apt installs. Mitigation: document it; subsequent sandboxes reuse the cached kit image.
- **Network policy denial during creation** — the install hook needs Debian/DHI package repositories. Mitigation: review `sbx env plan` and `sbx policy log`; the kit's install allowlist is part of its descriptor. Organization policy can still deny domains, in which case creation fails visibly rather than silently.
- **Global package/browser version skew** — a future project-local Playwright pin may need its matching browser. Mitigation: the kit allows the browser CDN at runtime and `/opt/ms-playwright` is writable; run `npx playwright install chromium` in that project. Firefox/WebKit still need native libraries and are out of scope.
- **Environment variable last-wins** — a later kit that sets `PLAYWRIGHT_BROWSERS_PATH` or `NODE_PATH` would override this kit. Mitigation: do not add such a kit; the current local kits do not set either.
- **Headless-only limitation** — no display server. Mitigation: document that headed mode, UI mode, and codegen are unsupported; screenshots, PDFs, traces, and videos work.
- **Pinned release ages** — `1.61.1` will eventually need an upgrade. Mitigation: explicit upgrade path above; a version bump is a separate change with recreation and verification.
- **Clone-mode handoff** — the `sbxenv.yaml` edit lives in the sandbox clone until retrieved. Mitigation: follow the repo's normal commit-and-host-pull workflow before `sbx env rm`.

## Migration Plan

1. Add the kit reference and documentation; run `sbx env plan` to review the kit and network diff.
2. Commit the change in the sandbox and retrieve it on the host before destroying the clone.
3. On the host: `sbx env rm` then `sbx env create`; run the verification commands above.
4. Record the result in `sbx/MIXINS.md` only after the successful recreation.
5. Rollback: remove the kit reference and recreate; no application data or external systems are involved.

## Open Questions

- Should the repository later adopt the `1.65.1-0` DHI packaging (newer Chromium/Playwright, but a required `version` argument and `deb/*` requirements)? Deferred until a probe sandbox validates the dependency contract. The spec and task breakdown do not depend on the answer.

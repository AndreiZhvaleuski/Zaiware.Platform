# Proposal

## Why

The sandbox can build and run the solution, but the agent cannot render a page, inspect browser console/network activity, take screenshots, or run end-to-end browser tests. Docker publishes a verified `sbx/playwright` mixin that packages the Playwright CLI, `@playwright/test`, and headless Chromium for exactly this purpose. Using the published kit is the official, maintained path: Docker's environment-file documentation uses `docker.io/sbx/playwright:1.61.1` as its example, and the kit supplies its own `agent-context@1` guidance, browser path, and network policy.

## What Changes

- Add `docker.io/sbx/playwright:1.61.1` to the `kits:` list in `sbxenv.yaml` as a published, verified mixin. Pin the exact version tag; do not use `latest`.
- Document the kit in `sbx/README.md`: what it provides, headless-only limits, the shared browser path, network expectations, verification commands, and the recreation requirement.
- Update the agent-context documentation and verification loop so `playwright` is treated as a kit whose context is staged under `/usr/share/sandbox/kit/playwright/` and indexed in the generated `AGENTS.md`.
- Add a short "published kits" note to `sbx/MIXINS.md`: prefer a verified published mixin when it covers the tool, pin it by version tag or digest, verify its capabilities against the workload, and do not create a duplicate local kit.
- Verify after a full `sbx env rm` + `sbx env create`: Playwright CLI version, a headless Chromium launch and screenshot, `PLAYWRIGHT_BROWSERS_PATH`, staged/indexed agent context, unchanged authenticated OpenCode server, and the expected network-policy entries.
- Record evidence only after that recreation succeeds.

**Non-goals:** no local `sbx/kits/playwright` copy, no Playwright MCP server, no project Playwright test scaffold, no headed/UI/codegen mode, no Firefox or WebKit, and no application-code changes.

## Capabilities

### New Capabilities

- `agent-browser-automation`: The sandbox provides a pinned, verified Playwright toolchain with headless Chromium so the agent can render pages, inspect browser behavior, take screenshots/PDFs/traces, and run Playwright tests inside the sandbox.

### Modified Capabilities

(none — this is a new sandbox capability; application specs are unaffected)

## Impact

- `sbxenv.yaml` — one published kit reference in `kits:`.
- `sbx/README.md` — layout, agent-context list, verify commands, and a Playwright section.
- `sbx/MIXINS.md` — published-kit guidance and, after verification, the evidence log.
- Sandbox lifecycle: the kit adds a ~292 MB image layer and install-time apt work, so the first create is slower; all kit changes take effect only after `sbx env rm` + `sbx env create`.
- Network policy: the kit adds install-time Debian/DHI package-repository access and runtime browser-CDN access. Sites under test still need their own policy grants.
- No application code, APIs, credentials, or project test files change.

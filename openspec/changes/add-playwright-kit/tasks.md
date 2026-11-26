# Tasks

## 1. Confirm and add the published Playwright kit

- [x] 1.1 Inspect the published kit descriptor before editing: `docker buildx imagetools inspect --raw docker.io/sbx/playwright:1.61.1 | jq -r '.annotations["vnd.docker.sandbox.kit.descriptor"] | fromjson'` (or read the Docker Hub tag page). Verify it is `schemaVersion: "3"`, `kind: mixin`, provides `playwright@1.61.1`, has no `requires`, has no required kit arguments, and declares `network-policy@1`, `lifecycle@1`, and `agent-context@1`.
- [x] 1.2 In `sbxenv.yaml`, add the pinned reference `docker.io/sbx/playwright:1.61.1` at the end of `kits:`, with a comment saying it is the published verified Playwright kit and must be pinned. Verify: `python3 -c 'import yaml; d=yaml.safe_load(open("sbxenv.yaml")); print("\n".join(map(str,d["kits"])))'` parses and lists the reference; `grep -n "sbx/playwright" sbxenv.yaml` shows exactly one entry and no `latest`.
- [x] 1.3 In `sbx/README.md`, add a layout-table row for the published kit and a `## Playwright` section. Cover: the exact pinned reference, what the kit provides (`playwright`, `@playwright/test`, headless Chromium), the ~292 MB layer and per-create install work, `/opt/ms-playwright` and the login-shell-only `PLAYWRIGHT_BROWSERS_PATH` mechanism, headless-only limits, network expectations, recreation requirement, and the `1.65.1-0` upgrade caveat (tag-based build-time version selection and `deb/*` requires). Verify: the section names the exact tag and both upgrade caveats; the layout table row links the reference to the section.
- [x] 1.4 Update the existing agent-context documentation and verification loop in `sbx/README.md`: change “four ordinary tool kits” to include `playwright`, add `playwright` to the staged-context/`AGENTS.md` index loops, and mention that the published kit stages `/usr/share/sandbox/kit/playwright/playwright-context.md`. Verify: the loop lists `provider-keys dotnet aspire openspec playwright opencode-server`, and the prose no longer says four ordinary kits.
- [x] 1.5 In `sbx/MIXINS.md`, add a short “published kits” note under the install/composition guidance: prefer a verified published mixin when it covers the tool, pin by version tag or digest, inspect capabilities and `requires`, verify against the workload, and do not create a duplicate local kit. Do not add an evidence-log entry yet. Verify: the note exists and the evidence table is unchanged.

## 2. Static validation and commit

- [x] 2.1 Run `openspec validate add-playwright-kit --strict` and fix any artifact warnings. Verify: the command exits 0.
- [x] 2.2 Review `git diff --check`, `git diff -- sbxenv.yaml sbx/README.md sbx/MIXINS.md openspec/changes/add-playwright-kit`, and confirm no unrelated files are staged. Verify: the diff contains only the kit entry, documentation, and coherent OpenSpec artifact changes.
- [x] 2.3 Commit the change inside the sandbox with a message such as `sbx: add pinned Playwright kit for headless browser automation`. Verify: `git show --stat HEAD` lists `sbxenv.yaml`, `sbx/README.md`, and `sbx/MIXINS.md` (plus the OpenSpec change if it is committed with the implementation).

## 3. Recreate and integration verification (host)

- [ ] 3.1 On the host, retrieve the sandbox commit before destroying the clone, then run `sbx env plan`. Verify: the plan shows the added `docker.io/sbx/playwright:1.61.1` kit and the expected Playwright install/runtime network domains; no unrelated changes appear.
- [ ] 3.2 On the host, run `sbx env rm` then `sbx env create`. Verify: creation succeeds without a kit resolution or package-repository error, and the boot hook still starts the OpenCode server.
- [ ] 3.3 Verify the toolchain: `sbx env exec -- playwright --version`, `sbx env exec -- bash -lc 'node -e "require(\"@playwright/test\"); console.log(\"resolved\")"'`, `sbx env exec -- bash -lc 'echo "$PLAYWRIGHT_BROWSERS_PATH"; test -w /opt/ms-playwright && echo writable'`, and `sbx env exec -- bash -c 'printf "non-login PBP=%s NODE_PATH=%s\n" "${PLAYWRIGHT_BROWSERS_PATH:-unset}" "${NODE_PATH:-unset}"'`. Verify: version matches 1.61.1, the module resolves in a login shell, the path is `/opt/ms-playwright`, the directory is writable, and the non-login probe documents the known `unset` limitation.
- [ ] 3.4 Verify a real headless render against localhost: serve a tiny HTML page with `python3 -m http.server 8765 --bind 127.0.0.1`, run `PLAYWRIGHT_BROWSERS_PATH=/opt/ms-playwright playwright screenshot --wait-for-timeout=1000 http://127.0.0.1:8765/ /tmp/pw-smoke.png` in a non-login shell, then run `test -s /tmp/pw-smoke.png && head -c 8 /tmp/pw-smoke.png | od -An -tx1 | grep -q "89 50 4e 47"`. Verify: the PNG exists, is non-empty, starts with the PNG magic bytes, and the explicit shared browser path works without a login shell.
- [ ] 3.5 Verify agent context: `sbx env exec -- test -s /usr/share/sandbox/kit/playwright/playwright-context.md`; `sbx env exec -- grep -q playwright /home/agent/.config/opencode/AGENTS.md`; run the README loop over `provider-keys dotnet aspire openspec playwright opencode-server`; and assert the Playwright context mentions `@playwright/test`, `/opt/ms-playwright`, headless operation, and external-site policy grants. Verify: all contexts are staged and indexed, and the Playwright guidance states all operating limits.
- [ ] 3.6 Verify no regression: authenticated `http://127.0.0.1:4096/openapi.json` returns `200`, anonymous returns `401`, `sbx env exec -- bash -lc 'git -C "$WORKSPACE_DIR" rev-parse --is-inside-work-tree'` confirms the clone workspace, and the persistent OpenCode volume still contains the existing session data. Verify: all checks pass.
- [ ] 3.7 Verify policy: run `sbx policy ls` (or inspect the create plan/log) and confirm the Playwright install repositories and runtime browser-CDN domains are present. Choose an external domain confirmed absent from the policy, attempt to load it with Playwright, and verify the request fails with a policy/proxy error. Verify: the expected domains are listed, the ungranted site fails, and no wildcard grant bypasses the policy.
- [ ] 3.8 Verify headless-only limits: confirm `DISPLAY` is unset, then run a headed screenshot and `playwright codegen` against a local page and verify both fail rather than opening a window. Verify: headless mode remains usable while headed/UI/codegen mode is unavailable.

## 4. Evidence and handoff

- [ ] 4.1 Only after 3.2–3.8 pass, add an evidence-log row to `sbx/MIXINS.md` recording the commit, the pinned kit, and what the recreation proved (toolchain, localhost screenshot, policy boundaries, headless-only behavior, context, server auth). Verify: the row exists and reflects actual commands/output, not an assumption.
- [ ] 4.2 Commit the evidence/doc update (if separate) and report the branch and commit hash. Verify: `git status` is clean and the final response gives the host retrieval command `git pull sandbox-zaiware-platform <branch>`.

## Workflow follow-up

- Retrieve the implementation on the host before `sbx env rm`; the clone is deleted with the sandbox.
- Keep the OpenCode data volume and sandbox name stable so sessions reattach after recreation.
- If a create fails on a network policy domain, inspect `sbx env plan` and `sbx policy log`; do not weaken the policy with `NO_PROXY` or a wildcard grant.
- Revisit the `1.65.1-0` upgrade only in a separate change with its own probe and recreation.
- Archive this change after the implementation and recreation verification are reviewed.

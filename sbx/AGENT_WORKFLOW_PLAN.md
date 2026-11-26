# Implementation plan: inject the sandbox workflow into OpenCode agent context

Status: **implemented; historical record.** The plan below documents the original
opencode-server workflow injection. Agent context now also covers the four
ordinary tool kits; see `sbx/README.md` and `sbx/MIXINS.md` for the current
rules. The Git handoff instructions below are superseded by `sbx/README.md` and
`sbx/kits/opencode-server/opencode-server-context.md`, which define the
host-side fetch/merge contract.

## 1. Objective

Configure the existing Docker Sandbox kits so that OpenCode receives persistent instructions describing this project's required workflow:

1. Always use the boot-managed OpenCode **server mode**.
2. Always use the sbx **clone workspace variant**.
3. After every completed file-changing task, the agent must commit its work and ask the user to retrieve it on the host through the Docker sbx remote.

The generated instructions must be delivered through the kit `agent-context@1` capability. Do not replace this with a manually maintained repository-root `AGENTS.md`.

## 2. Important Git handoff clarification

The Docker sbx remote named `sandbox-zaiware-platform` exists on the **host**. It lets the host fetch commits from the sandbox. It is not an in-sandbox remote to which the agent can push.

Therefore, implement this supported workflow:

1. The agent commits the completed work on its current branch inside the sandbox.
2. The agent reports the branch and commit hash.
3. The agent asks the user to run the following from the host repository, with the corresponding branch checked out:

   ```powershell
   git pull sandbox-zaiware-platform <branch>
   ```

Do **not** instruct the agent to run `git push sandbox-zaiware-platform` inside the sandbox. That remote is not available there.

Pushing to an external shared remote such as `origin` is outside this change. It would be a separate workflow and could require additional credential and network configuration.

## 3. Current configuration that must remain intact

The requested server and clone behavior is already configured:

- `sbxenv.yaml` selects `agent: shell` and includes `./sbx/kits/opencode-server`.
- `sbx/kits/opencode-server/opencode-server.yaml` starts `opencode serve --hostname 0.0.0.0 --port 4096` in a background lifecycle startup hook.
- `long-running@1` keeps the sandbox alive after clients disconnect.
- `sbxenv.yaml` publishes sandbox port `4096` to `127.0.0.1:4096` on the host.
- `sbxenv.yaml` already contains:

  ```yaml
  workspace:
    path: .
    clone: true
  ```

Do not redesign or replace these mechanisms. The main implementation is the addition of agent context and documentation.

## 4. Files to change

### 4.1 Add `sbx/kits/opencode-server/opencode-server-context.md`

Create this new Markdown file beside `opencode-server.yaml` and `opencode-server.dockerfile`.

Use the following content as the required baseline. Wording may be tightened, but none of the rules may be dropped:

~~~markdown
# Zaiware.Platform sandbox workflow

These rules apply to work performed in this Docker Sandbox.

## Server-mode operation

- Normal operation uses the OpenCode API server started by the `opencode-server` kit's lifecycle startup hook.
- The server listens on sandbox port `4096`; the host connects to `127.0.0.1:4096` using the configured credentials.
- Do not start a second `opencode serve` process or substitute an interactive OpenCode TUI/session for the normal server workflow.
- Do not treat `sbx env run` as the normal way to start the environment. On the host, normal startup is `sbx env create`, which starts the sandbox without attaching and lets the boot hook launch the server.
- `sbx` commands are host commands. Do not assume the `sbx` CLI is installed or usable inside the sandbox.

## Clone-only workspace

- Work in the private Git clone at `$WORKSPACE_DIR`.
- The host source mounted at `/run/sandbox/source` is read-only. Never attempt to edit it or use it as the working tree.
- This environment must use clone mode. Do not request or recommend direct-mount mode, `workspace.clone: false`, or `--clone=false` for normal operation.
- Host and sandbox changes meet through Git commits and retrieval, not through direct file synchronization.

## Completing file-changing tasks

After completing and verifying a task that changes files:

1. Review `git status` and `git diff` and stage only intended files.
2. Commit the changes with a clear message describing the completed task. The user's standing workflow instruction authorizes these task-completion commits; do not leave completed work merely uncommitted in the sandbox.
3. Do not create an empty commit when a task made no file changes.
4. Confirm the resulting branch and commit with `git branch --show-current` and `git rev-parse --short HEAD`, and check that no intended changes remain uncommitted.
5. In the final response, report the commit hash, branch, and verification performed.
6. Explicitly ask the user to retrieve the work on their machine. Provide this host command, substituting the real branch name:

   ```powershell
   git pull sandbox-zaiware-platform <branch>
   ```

   Tell the user to run it from the host repository with the corresponding branch checked out.
7. Do not run `git push sandbox-zaiware-platform` inside the sandbox: that remote is host-side and is used by the host to fetch from the sandbox.
8. Do not remove or recreate the sandbox before the user has retrieved commits that exist only in its private clone.
~~~

### 4.2 Update `sbx/kits/opencode-server/opencode-server.yaml`

Add the following entry under the existing top-level `capabilities:` list. Place it before the existing `volume@1` entry for readability:

```yaml
  - type: com.docker.sandbox/agent-context@1
    config:
      filename: AGENTS.md
      directory: /home/agent/.config/opencode
      contentFile: ./opencode-server-context.md
```

This explicit destination is required because `opencode-server` is the agent mixin and OpenCode discovers global instructions at:

```text
/home/agent/.config/opencode/AGENTS.md
```

The kit frontend stages `contentFile` automatically. **Do not modify `opencode-server.dockerfile`** to copy this Markdown file manually.

Also update the kit's top-level `description` so it mentions the injected sandbox workflow instructions in addition to the binary, server startup, and persistent session data.

Preserve all existing capabilities, arguments, lifecycle hooks, credentials-related comments, and server settings.

Compatibility constraint: use `agent-context@1`, consistent with the repository's documented sbx v0.50 target. Do not silently migrate the kit to `agent-context@2` or raise the minimum sbx version as part of this task.

### 4.3 Update `sbxenv.yaml` comments only

No structural configuration change is required in this file.

Update the relevant comments so they clearly state:

- `sbx env create` is the required normal startup workflow because the server is boot-managed.
- Attached `sbx env run` is not the normal server startup path.
- Clone mode is mandatory for the normal workflow; do not override it with `--clone=false`.
- The `opencode-server` kit also supplies the generated OpenCode workflow instructions through `agent-context@1`.

Keep these values unchanged:

```yaml
agent: shell
workspace:
  path: .
  clone: true
```

Keep the kit list, secret mount, port publication, and bindings unchanged.

Note: the file's own comments say sbx mounts it read-only inside the sandbox. If that protection is active during implementation, this comment-only edit must be made from the host rather than bypassing the mount protection.

### 4.4 Update `sbx/README.md`

Make focused documentation updates; do not rewrite unrelated sections.

1. Add `kits/opencode-server/opencode-server-context.md` to the layout table and describe it as the injected server/clone/Git-handoff workflow.
2. Update the `opencode-server.yaml` layout description to mention the OpenCode `AGENTS.md` context profile.
3. In the OpenCode API server section, state that normal operation always uses the boot-managed server started by `sbx env create`.
4. Add a short **Agent workflow and Git handoff** subsection covering:
   - The generated profile path `/home/agent/.config/opencode/AGENTS.md`.
   - The fact that `agent-context@1` adds a kit index entry pointing to the staged context file; it does not create a repository-root `AGENTS.md`.
   - Clone-only editing in `$WORKSPACE_DIR`.
   - The agent's commit requirement after file-changing tasks.
   - The host-side nature of `sandbox-zaiware-platform`.
   - The exact host retrieval command:

     ```powershell
     git pull sandbox-zaiware-platform <branch>
     ```

   - The requirement to run that command with the corresponding host branch checked out.
   - The warning not to remove the sandbox before retrieving commits that exist only in its clone.
5. Add agent-context verification to the existing verification instructions.
6. State that changing the kit context requires a full sandbox recreation before it takes effect.

### 4.5 Update `sbx/MIXINS.md`

Make a small authoring-guide update so future kit changes preserve this behavior:

- Explain that an agent mixin can declare `filename` together with an explicit `directory`, while an ordinary tool mixin normally contributes only a body.
- Record OpenCode's explicit profile destination as `/home/agent/.config/opencode/AGENTS.md`.
- Explain that `contentFile` is staged by the frontend and must not also be copied by the Dockerfile.
- Reiterate that agent context is applied at create time and requires recreation to verify.
- Do not add a fabricated entry to the evidence log. Add evidence only after the change has actually been built/recreated and verified.

## 5. Explicit non-goals

Do not:

- Add a new workflow mixin; the existing `opencode-server` agent mixin owns this context.
- Add a repository-root `AGENTS.md`.
- Modify the OpenCode binary version.
- Modify the server port, hostname, password handling, secrets, bindings, or volume.
- Replace the lifecycle server with `cmd:`, an attached foreground process, or a TUI.
- Change `workspace.clone` to `false`.
- Add an external `origin` push requirement or new Git credentials.
- Change unrelated .NET, Aspire, OpenSpec, or provider-key kit configuration.

## 6. Verification procedure

### 6.1 Static checks

Before recreation:

- Confirm the new context file exists beside the kit descriptor.
- Confirm `opencode-server.yaml` still parses as YAML and contains exactly one new `agent-context@1` entry with the required filename, directory, and content file.
- Confirm there were no functional `sbxenv.yaml` changes.
- Confirm the Dockerfile was not changed.

### 6.2 Review and recreate on the host

Retrieve any existing sandbox-only commits before destroying the old private clone. Then run on the host:

```powershell
sbx env plan
sbx env rm
sbx env create
```

A full recreation is required; `sbx env exec` or attaching to the existing sandbox does not apply new agent context.

If creation rejects the explicit `directory` field, record the host `sbx --version` and the exact error. Do not silently move the instructions to a different profile or change capability versions.

### 6.3 Verify generated context

Run on the host:

```powershell
sbx env exec -- test -s /home/agent/.config/opencode/AGENTS.md
sbx env exec -- cat /home/agent/.config/opencode/AGENTS.md
sbx env exec -- test -s /usr/share/sandbox/kit/opencode-server/opencode-server-context.md
```

Check that:

- The generated `AGENTS.md` contains the kit index/reference for `opencode-server`.
- The staged context contains server-mode, clone-only, commit, and host-pull instructions.
- No repository-root `AGENTS.md` was generated as part of this mechanism.

### 6.4 Verify server behavior did not regress

Use the existing authenticated check from `sbx/README.md`:

- Authenticated `http://127.0.0.1:4096/openapi.json` returns `200`.
- Anonymous access returns `401`.
- The running server is the lifecycle-managed `opencode serve` process; no duplicate server was introduced.

### 6.5 Verify Git handoff

After the implementation's own commit inside the sandbox:

- Record `git branch --show-current` and `git rev-parse --short HEAD`.
- Confirm `git status` shows no intended changes left uncommitted.
- On the host, confirm the remote exists:

  ```powershell
  git remote get-url sandbox-zaiware-platform
  ```

- With the corresponding host branch checked out, retrieve the branch:

  ```powershell
  git pull sandbox-zaiware-platform <branch>
  ```

Do not test or document an in-sandbox push to `sandbox-zaiware-platform`.

## 7. Original acceptance criteria

Retained as the historical checklist for the original change.

The task is complete only when all of the following are true:

- [ ] `opencode-server-context.md` exists and contains all required workflow rules.
- [ ] `opencode-server.yaml` declares the explicit OpenCode `agent-context@1` profile and staged context file.
- [ ] `opencode-server.dockerfile` is unchanged.
- [ ] `sbxenv.yaml` still uses the existing server kit and `clone: true`; only its explanatory comments changed.
- [ ] `sbx/README.md` documents the generated context and correct host-side Git retrieval workflow.
- [ ] `sbx/MIXINS.md` records the relevant authoring and recreation rules.
- [ ] A full host-side recreation succeeds.
- [ ] `/home/agent/.config/opencode/AGENTS.md` references the new context after recreation.
- [ ] Authenticated server health behavior remains `200`, and anonymous access remains `401`.
- [ ] The implementation is committed in the sandbox, and the final response gives the user the exact host `git pull sandbox-zaiware-platform <branch>` command with the real branch name.

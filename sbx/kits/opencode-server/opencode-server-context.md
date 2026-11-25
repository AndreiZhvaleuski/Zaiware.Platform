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

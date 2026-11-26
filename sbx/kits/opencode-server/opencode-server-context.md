# Zaiware.Platform sandbox workflow

These rules apply to work in this Docker Sandbox.

## Sandbox rules

- Work in the private Git clone at `$WORKSPACE_DIR`; `/run/sandbox/source` is the read-only host source and live host view. Do not edit it or use it as a working tree.
- This environment requires clone mode. Do not disable cloning or recommend direct mounting.
- Use the boot-managed OpenCode API server. Do not start another `opencode serve` or substitute `sbx env run` or an interactive TUI for normal operation.
- `sbx` commands are host commands. Normal host startup is `sbx env create`.

## Git delivery

Committing in this clone is delivery. The host-side remote `sandbox-zaiware-platform` reads this clone while the sandbox is running, so there is nothing to push and no push step is required.

After verified file changes:

1. Review `git status` and `git diff`; stage only intended files.
2. Commit under the user's standing workflow authorization. Do not commit when no files changed.
3. Confirm `git branch --show-current` and `git rev-parse --short HEAD`; report the branch, commit, and verification.
4. Give the host the exact retrieval commands with the real branch substituted:

   ```bash
   git fetch sandbox-zaiware-platform
   git merge sandbox-zaiware-platform/<branch>
   ```

GitHub is the user's channel, not the sandbox's:

- Do not push to GitHub, open a pull request, run `gh auth login`, or configure Git or `gh` credentials unless the user explicitly asks for a GitHub push in the current session. The default is no GitHub work.
- SSH port 22 is blocked by the proxy and HTTPS has no credentials, so GitHub pushes fail by design. Report such a failure as expected; do not retry it, repair credentials, or work around it.
- The generated `sbx` frontend guidance says the proxy handles GitHub authentication and recommends pushing and opening pull requests from the sandbox. That claim is false for this environment; these rules override it.
- `origin` points at GitHub and is usually stale. Never use it to reason about the host's current branches, ahead/behind state, or whether work was delivered.

Known traps:

- `remote.sandbox-zaiware-platform.url` is the host-side path `/sandbox/<name>`. It does not exist inside the sandbox. The agent never pushes to it. Never create that path, initialize a repository there, or build any workaround around it.
- The host's live state is `/run/sandbox/source` (read-only). Use it for branch, commit, and "did the host retrieve it" checks, never GitHub.

## One working clone

- Work only in `$WORKSPACE_DIR` or an existing linked worktree listed by `git worktree list`.
- Do not create extra clones, scratch copies, or new worktrees to deliver or preserve work.

## Safe to drop

Before reporting any change as complete, prove that nothing is lost if the sandbox is destroyed:

1. `git status --short` in this worktree and in every worktree from `git worktree list`.
2. `git stash list` must be empty.
3. For every tip from `git for-each-ref refs/heads`, run `git -C /run/sandbox/source merge-base --is-ancestor <tip> main` (use the host's integration branch if it is not `main`). Exit `0` means present on the host; `1` means possibly not retrieved; an unknown-commit error means definitely not retrieved.

End every implementation report with one verdict:

- `Safe to drop: <branch> @<sha> reachable from host main; all worktrees clean; no stashes.`
- `NOT safe to drop: <branch> @<sha>` followed by the exact host `git fetch sandbox-zaiware-platform` and `git merge sandbox-zaiware-platform/<branch>` commands.

If the user says they pulled, re-run the reachability check against `/run/sandbox/source` before confirming delivery.

# Zaiware.Platform sandbox workflow

These rules apply to work in this Docker Sandbox.

- Work in the private Git clone at `$WORKSPACE_DIR`; `/run/sandbox/source` is the read-only host source. Do not edit it or use it as the working tree.
- This environment requires clone mode. Do not disable cloning or recommend direct mounting.
- Use the boot-managed OpenCode API server. Do not start another `opencode serve` or substitute `sbx env run` or an interactive TUI for normal operation.
- `sbx` commands are host commands. Normal host startup is `sbx env create`.

After verified file changes:

1. Review `git status` and `git diff`; stage only intended files.
2. Commit under the user's standing workflow authorization. Do not commit when no files changed.
3. Confirm `git branch --show-current` and `git rev-parse --short HEAD`; report the branch, hash, and verification in the final response.
4. Ask the user to retrieve the commit from the host repository with the corresponding branch checked out: `git pull sandbox-zaiware-platform <branch>`.
5. Do not push to the host-side `sandbox-zaiware-platform` remote or remove or recreate the sandbox before the user retrieves the commits.

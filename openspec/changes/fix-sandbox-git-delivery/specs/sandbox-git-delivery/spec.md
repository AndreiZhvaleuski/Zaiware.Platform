# Spec Delta

## Purpose

Defines how work produced in the clone-mode `sbx` sandbox reaches the host: the agent commits in the sandbox clone, the host fetches from the host-side sandbox remote, and the agent verifies delivery without using GitHub, fabricating remotes, or leaving work only in the sandbox.

## ADDED Requirements

### Requirement: Committing in the sandbox is the delivery action

The sandbox agent guidance SHALL define a commit in the sandbox clone as the complete delivery action. It MUST tell the agent that the host-side remote `sandbox-<name>` reads the sandbox clone directly while the sandbox is running, so the agent has nothing to push, and it MUST give the host retrieval commands as `git fetch sandbox-<name>` followed by `git merge sandbox-<name>/<branch>` using the exact branch name.

#### Scenario: Completed change is reported with retrieval commands

- **WHEN** the agent completes a file-changing task in the sandbox
- **THEN** the report names the branch and commit that contain the work
- **AND** the report gives the host commands `git fetch sandbox-<name>` and `git merge sandbox-<name>/<branch>` with the real branch substituted

#### Scenario: No push step is required or attempted

- **WHEN** the agent follows the delivery guidance for ordinary task completion
- **THEN** the guidance states that committing is delivery
- **AND** no push to any remote is part of the required workflow

### Requirement: GitHub delivery is prohibited by default

The guidance MUST prohibit pushes to GitHub, pull requests, `gh` login, and Git or `gh` credential configuration from inside the sandbox unless the user explicitly asks for a GitHub push in the current session.

#### Scenario: Agent avoids GitHub delivery

- **WHEN** the agent completes work and the user has not explicitly requested a GitHub push in the current session
- **THEN** the agent does not push to GitHub, open a pull request, run `gh auth login`, or configure credentials
- **AND** it delivers the work through the sandbox commit instead

#### Scenario: Explicit request permits GitHub work

- **WHEN** the user explicitly asks for a GitHub push in the current session
- **THEN** the agent may attempt the requested GitHub operation
- **AND** the default prohibition does not override the user's explicit instruction

### Requirement: GitHub authentication failures are expected

The guidance MUST state that SSH port 22 is blocked by the proxy and HTTPS has no credentials, so GitHub pushes fail by design and the agent must not retry them or work around them. It MUST also identify the generated `sbx` frontend claim that the proxy handles GitHub authentication as false for this environment.

#### Scenario: A push failure is treated as expected

- **WHEN** a Git operation against GitHub fails because SSH is blocked or HTTPS has no credentials
- **THEN** the agent reports the failure as the documented environment behavior
- **AND** it does not retry, attempt credential repair, or suggest a sandbox credential setup as the next step

#### Scenario: The generated authentication claim is overridden

- **WHEN** the generated `sbx` frontend guidance says the proxy handles GitHub authentication and recommends pushing from the sandbox
- **THEN** the repo guidance states that claim is false here
- **AND** the agent follows the commit-and-host-fetch contract instead

### Requirement: Host state is read from the live read-only mount

The guidance SHALL direct the agent to `/run/sandbox/source` as the only authority for the host's current state and MUST forbid using GitHub to update or verify that state. It MUST state that `origin` points at GitHub, is usually far behind the host, and is not refreshed from GitHub for ahead/behind or delivery checks.

#### Scenario: Agent checks whether the host has the work

- **WHEN** the agent needs to know the host's current branches or whether a commit reached the host
- **THEN** it runs the check against `/run/sandbox/source`
- **AND** it does not fetch from GitHub to reason about host state

#### Scenario: GitHub history is used only as stale history

- **WHEN** the agent inspects `origin` in the sandbox clone
- **THEN** the guidance identifies it as GitHub history that may lag the host
- **AND** it does not treat `origin/main` as the host's main branch

### Requirement: The host-side sandbox remote is never created or pushed to

The guidance MUST state that `remote.sandbox-<name>.url` of `/sandbox/<name>` is a host-side path that does not exist inside the sandbox, and MUST forbid creating that path, initializing a repository there, pushing to it, or building any workaround around it.

#### Scenario: Agent sees the host-side remote path

- **WHEN** the agent reads the host Git configuration through `/run/sandbox/source/.git/config` and sees `/sandbox/<name>`
- **THEN** it treats the path as host-side and unavailable in the sandbox
- **AND** it does not try to push to it or create it

#### Scenario: Missing remote path does not trigger a workaround

- **WHEN** a command that references `/sandbox/<name>` inside the sandbox fails because the path is absent
- **THEN** the agent reports the path as host-side
- **AND** it does not initialize a bare repository or any other substitute at that path

### Requirement: Delivery uses a single working clone

The guidance MUST confine work to the clone at `$WORKSPACE_DIR` or an existing registered linked worktree. It MUST forbid creating additional clones, temporary copies, or new worktrees for the purpose of delivering or preserving work.

#### Scenario: Agent delivers from the current worktree

- **WHEN** the agent needs to preserve or deliver completed work
- **THEN** it commits in the current clone or an existing linked worktree
- **AND** it does not create another clone in `/tmp`, create a new worktree, or copy the repository elsewhere

### Requirement: Safe-to-drop checks precede every completion report

Before reporting any change as complete, the agent SHALL verify that nothing can be lost if the sandbox is destroyed: `git status --short` in the working tree and in every linked worktree listed by `git worktree list`, `git stash list`, and reachability of every branch tip from `git for-each-ref refs/heads` against the host's integration branch with `git -C /run/sandbox/source merge-base --is-ancestor <tip> main`.

#### Scenario: Dirty files or stashes block a safe verdict

- **WHEN** any working tree has uncommitted changes or any stash exists
- **THEN** the report does not declare the sandbox safe to drop
- **AND** it names the dirty paths or stashes that must be committed or reported

#### Scenario: Branch reachability is classified

- **WHEN** the agent checks a branch tip with `git -C /run/sandbox/source merge-base --is-ancestor <tip> main`
- **THEN** exit `0` means the tip is present on the host
- **AND** exit `1` means it may not have been pulled
- **AND** an unknown-commit error means it was definitely not pulled

### Requirement: Reports end with an explicit delivery verdict

Every implementation report SHALL end with either `Safe to drop` plus the evidence checked, or `NOT safe to drop` naming each outstanding branch and commit with the exact host `git fetch` and `git merge` commands. When the user says they have pulled, the agent MUST re-run the reachability check against `/run/sandbox/source` before confirming delivery.

#### Scenario: Safe verdict is evidence-backed

- **WHEN** all branches are reachable from the host integration branch, no working tree is dirty, and no stash exists
- **THEN** the report ends with a `Safe to drop` verdict that states those facts

#### Scenario: Unsafe verdict names the outstanding work

- **WHEN** a branch tip is not reachable from the host integration branch
- **THEN** the report ends with a `NOT safe to drop` verdict naming the branch and commit
- **AND** it includes the exact host commands to retrieve that branch

#### Scenario: A claimed pull is verified, not trusted

- **WHEN** the user says they have pulled the delivered branch
- **THEN** the agent re-checks the branch tip against `/run/sandbox/source`
- **AND** it confirms delivery only if the tip is reachable there

### Requirement: Repository documentation states the delivery contract

The repository documentation SHALL describe the clone-mode handoff as a host `git fetch sandbox-<name>` plus `git merge sandbox-<name>/<branch>`, identify `/run/sandbox/source` as a live read-only view of the host, state that GitHub is the user's channel and not the sandbox's, and warn that removing the sandbox before retrieval discards sandbox-only commits.

#### Scenario: Documentation matches the verified workflow

- **WHEN** an operator reads the sandbox handoff documentation
- **THEN** it gives the fetch and merge commands, not a sandbox push
- **AND** it explains the live read-only host view and the recreation warning

#### Scenario: Documentation does not promise GitHub delivery

- **WHEN** the sandbox documentation is searched for GitHub push, pull-request, or credential instructions
- **THEN** no such instruction appears as a supported sandbox workflow
- **AND** any mention of GitHub identifies it as the user's own channel

### Requirement: Corrected guidance is staged and indexed after recreation

The corrected agent guidance SHALL be the content staged from `sbx/kits/opencode-server/opencode-server-context.md` under `/usr/share/sandbox/kit/opencode-server/` and indexed by the generated `/home/agent/.config/opencode/AGENTS.md`. The kit index entry MUST identify the context as the owner of Git delivery so the agent can find it. The guidance takes effect only after the sandbox is recreated.

#### Scenario: Recreated sandbox exposes the corrected guidance

- **WHEN** the sandbox is recreated after the guidance change
- **THEN** the staged `opencode-server-context.md` exists and contains the delivery contract and safe-to-drop protocol
- **AND** the generated `AGENTS.md` indexes the `opencode-server` kit context with a Git delivery description

#### Scenario: Attaching without recreation does not apply the change

- **WHEN** the operator attaches to an existing sandbox with `sbx env exec` or `sbx env run` instead of recreating it
- **THEN** the documentation identifies that the new guidance is not applied
- **AND** a full `sbx env rm` and `sbx env create` is the documented way to apply it

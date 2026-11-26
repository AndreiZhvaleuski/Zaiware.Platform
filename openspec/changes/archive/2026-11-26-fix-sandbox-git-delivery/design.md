# Design

## Context

See `proposal.md` — Why for the observed failures. The constraints that shape the approach:

- This repository mandates clone mode (`sbxenv.yaml`: `workspace.clone: true`, sandbox name `zaiware-platform`). The sandbox works in `$WORKSPACE_DIR`; the host repository is bind-mounted read-only at `/run/sandbox/source` and is live, not a snapshot.
- The host-side remote is `sandbox-zaiware-platform` with URL `/sandbox/zaiware-platform`. Its fetch refspecs are `+refs/heads/*:refs/remotes/sandbox-zaiware-platform/*` and `+refs/heads/*:refs/sandboxes/zaiware-platform/*`. The remote is served by the host and reads the sandbox clone while the sandbox is up. It does not exist inside the sandbox.
- The repo-controlled agent guidance is `sbx/kits/opencode-server/opencode-server-context.md`, staged at `/usr/share/sandbox/kit/opencode-server/opencode-server-context.md` and indexed by the generated `/home/agent/.config/opencode/AGENTS.md`. It is not inlined into `AGENTS.md`; it is read from the index when the kit is relevant. Agent context is applied only at sandbox create time.
- The same generated `AGENTS.md` also contains a `<!-- sbx:guidance -->` block produced by the `sbx` frontend. That block includes a "Git Authentication" section claiming the proxy handles GitHub authentication and telling the agent to push and open pull requests from inside the sandbox. It is generated outside this repository and cannot be edited here.
- Current repo docs (`sbx/README.md`, `sbxenv.yaml` comments) describe host retrieval as `git pull sandbox-zaiware-platform <branch>` "with the corresponding branch checked out". That is compatible with the remote but less precise than the verified fetch/merge contract, and it does not warn about the GitHub or `/sandbox/<name>` traps.
- There is no existing spec capability covering sandbox Git delivery; `agent-browser-automation` and `otp-login` are unrelated.

## Goals / Non-Goals

**Goals:**

- Make `opencode-server-context.md` the single, actionable statement of the delivery contract: commit is delivery; the host fetches and merges; GitHub is not a delivery or synchronization route; `/sandbox/<name>` is host-side only; one working clone; safe-to-drop verification before every completion report.
- Make the generated kit index say that this context owns Git delivery, so an agent looking for handoff rules finds it.
- Bring the operator-facing docs (`sbx/README.md`, `sbxenv.yaml` comments) into agreement with the same contract.
- Keep the change guidance-only: no hooks, scripts, remotes, kits, network grants, or environment changes.

**Non-Goals:**

- Editing or suppressing the `sbx` frontend guidance block. It is generated outside this repo; this change can only contradict it explicitly.
- Changing the `sandbox-zaiware-platform` remote, its refspecs, or `origin`.
- Automating the host-side fetch/merge; the agent cannot run host commands, and the user deliberately performs retrieval.
- Supporting or recommending direct-mode workspaces. Direct mode is mentioned only to explain the mode check and the uncommitted-work caveat.
- Rewriting the body of `sbx/AGENT_WORKFLOW_PLAN.md`. It is explicitly a historical record of the original context injection; only its status line gains a pointer to the current contract in the kit context and `sbx/README.md`.

## Decisions

### D1 — Put the contract in the existing `opencode-server` kit context

Update `sbx/kits/opencode-server/opencode-server-context.md` rather than adding a repo-root `AGENTS.md`, a new kit, or an `.opencode/opencode.json` `instructions` entry.

Rationale: the repo already documents `agent-context@1` as the only agent-instruction mechanism, explicitly forbids a repo-root `AGENTS.md`, and the `opencode-server` kit owns the generated global profile. An `instructions` entry would introduce a second, competing source and change OpenCode configuration for a guidance-only fix.

Alternative considered: a new `git-delivery` kit. Rejected — duplicate mechanism, extra recreation surface, and no new capability is needed.

### D2 — Treat the frontend's "Git Authentication" claims as false and say so

The kit context must open by stating that GitHub pushes are not part of this workflow and that the observed authentication failures are expected, not a defect to fix. The kit description in `opencode-server.yaml` will mention Git delivery so the generated index signals the correction.

Rationale: the frontend block is outside the repo and remains authoritative-looking in the same `AGENTS.md`. Since it cannot be removed, the repo-controlled context must contradict it explicitly rather than silently omit the topic. Merely deleting GitHub mentions from our docs would leave the false claim unopposed.

Alternative considered: rely on the agent reading the kit context by chance. Rejected — the index description must make the relevance obvious for Git questions.

### D3 — Standardize host retrieval on `git fetch` + `git merge`

Document and report `git fetch sandbox-zaiware-platform` followed by `git merge sandbox-zaiware-platform/<branch>`.

Rationale: the host refspec already fetches every sandbox branch into `refs/remotes/sandbox-zaiware-platform/*`; a plain fetch is sufficient and explicit. The merge target is the host's current branch, so the old "with the corresponding branch checked out" instruction is unnecessary and misleading. `git pull sandbox-<name> <branch>` still works, but it hides which ref is fetched and inherits the user's pull/merge configuration, so it is not the documented standard.

Alternative considered: keep `git pull` to avoid doc churn. Rejected — it is the command most likely to be copied without understanding the refspec, and it conflicts with the "plain fetch grabs all sandbox branches" model the safe-to-drop check relies on.

### D4 — Use `/run/sandbox/source` as the host-state oracle, never GitHub

All ahead/behind, branch-presence, and "did the user pull?" checks run against the live read-only mount. `origin` is identified as GitHub history that may lag the host, and `git fetch origin` is expressly not a way to refresh host state.

Rationale: GitHub is a downstream publication channel managed by the user; the host can be far ahead. A fetch from GitHub succeeds but produces wrong conclusions (observed). The mount gives exact host state with no network and no side effects.

Reachability check: `git -C /run/sandbox/source merge-base --is-ancestor <tip> main`. Exit 0 = present; exit 1 = possibly not pulled; an unknown-commit error = definitely not pulled. This is conservative: if the user merged the branch into a non-main integration branch, the check reports a false negative, which prompts an unnecessary but harmless fetch/merge command. That is preferable to a false "safe" verdict. The documentation says "main or the branch the work is meant to land on" where the host uses a different integration branch.

Alternative considered: compare commit hashes or `git branch --contains`; rejected as more commands and more output for the same answer.

### D5 — Make safe-to-drop a report gate with a fixed verdict format

Before every completion report: `git status --short` in the working tree and in every worktree from `git worktree list`, `git stash list`, and the reachability check for every tip in `git for-each-ref refs/heads`. The report ends with one of the two approved verdicts and, when work is outstanding, the exact host command.

Rationale: uncommitted files and unretrieved branches are the only ways work is actually lost when the sandbox is destroyed. A fixed verdict makes omission visible and gives the user a copy-paste recovery path. Treating "the user said they pulled" as unverified would repeat the observed failure of ending without confirmation, so a claimed pull triggers a re-check.

Alternative considered: only check `git status` (misses committed-but-unretrieved branches); only ask the user (unverifiable and was already skipped in practice).

### D6 — Allow existing worktrees, forbid new ones and copies

The guidance permits committing in `$WORKSPACE_DIR` or an existing registered linked worktree, and forbids creating new clones, `/tmp` copies, or new worktrees to deliver work.

Rationale: linked worktrees share the clone's refs, so a branch committed in a parallel session is already visible to the main clone and to the host fetch; only each worktree's uncommitted files are at risk, which the dirty check covers. Creating copies was the direct cause of the four-repo, 30-minute delivery session.

Alternative considered: forbid worktrees entirely. Rejected — existing sessions may legitimately run in one, and worktrees do not endanger committed refs.

### D7 — Keep the context concise; put rationale in README/design

`sbx/MIXINS.md` requires agent context to contain only non-obvious, actionable rules. The context file will use short imperatives, the two trap callouts, the exact commands, and the verdict template; the reasoning, evidence, and operational detail stay in `sbx/README.md` and this design.

Rationale: long context is less likely to be read and more likely to drift. The README remains the operator-facing explanation.

### D8 — Verification is content + post-recreation

Because this is guidance, there is no runtime test. Verification is: the staged context contains the required statements after a full recreate; the generated `AGENTS.md` indexes `opencode-server` with a Git delivery description; the README and `sbxenv.yaml` comments contain no supported GitHub-push instruction and use the fetch/merge contract; and a fresh agent session asked to deliver a trivial commit produces the expected commands and verdict.

Rationale: static checks catch drift; a post-recreate session check proves the guidance actually reaches the agent, which file existence alone does not.

## Risks / Trade-offs

- **The contradictory frontend "Git Authentication" block remains in every generated `AGENTS.md`.** → The kit context explicitly says it is false for this environment, and the index description advertises Git delivery. If sessions still follow the frontend claim, that requires a separate upstream `sbx`/frontend change; it is outside this repo's control.
- **Kit context is only read when the agent considers the kit relevant.** → The generated index entry will name Git delivery explicitly, and `sbx/README.md` points operators at the same contract. The post-recreate session check verifies the behavior, not just the file.
- **Reachability against `main` yields false negatives for work merged to another branch.** → Document checking the intended integration branch and keep the unsafe verdict conservative; the user can confirm with one glance at the host.
- **The user may have local host commits that make `/run/sandbox/source` differ from their working tree** (for example, uncommitted host changes). → The mount reflects committed host state; the check answers "is the commit reachable from host main", which is the delivery question. Uncommitted host work is outside this contract.
- **Old docs or habits still say `git pull sandbox-...`.** → Update `sbx/README.md` and `sbxenv.yaml` comments; the old command remains functional, so no user is broken, but all current guidance converges on fetch/merge.
- **Guidance changes do not apply until recreation.** → Docs and tasks require `sbx env rm` + `sbx env create` before verification; attaching is explicitly documented as insufficient.
- **Losing sandbox-only commits during the change's own recreation.** → The implementing session must apply the safe-to-drop protocol before reporting, and the operator must retrieve the branch before `sbx env rm`. This is the same risk the change exists to prevent, so the change must demonstrate the protocol on itself.

## Migration Plan

1. Edit the kit context, kit description, README, and `sbxenv.yaml` comments; keep application code untouched.
2. Commit the change in the sandbox and report the branch with the fetch/merge commands and a safe-to-drop verdict.
3. On the host: review and merge the branch, then run `sbx env rm` + `sbx env create`.
4. After recreation: confirm `/usr/share/sandbox/kit/opencode-server/opencode-server-context.md` contains the contract and the generated `AGENTS.md` index mentions Git delivery; start a fresh session and ask it to describe how it would deliver a commit. Expected: commit, host fetch/merge, no GitHub, safe-to-drop verdict.
5. Rollback: revert the guidance/docs files and recreate. No data migration and no compatibility surface; the previous guidance returns after recreation.

## Open Questions

- Should the contradictory `sbx` frontend guidance be reported upstream or pinned to a fixed `sbx` release? Deferred: it does not change this change's specs, approach, or tasks, and the repo-side override is sufficient to proceed.

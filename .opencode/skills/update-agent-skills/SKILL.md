---
name: Update Agent Skills
description: Update the vendored Duende and .NET skill packs and the pinned Aspire skill catalog. Use when the user asks to update the agent skills, refresh or bump the skill packs, or check the installed packs for upstream drift.
---

# Update Agent Skills

The manifest `.opencode/skills/sources.json` records every upstream repository,
its immutable ref, the included components, and the vendored content hashes.
Never edit vendored skill files by hand; update them through the sync script so
the pins and hashes stay consistent.

## 1. Check for drift

```bash
bash .opencode/skills/sync-agent-skills.sh --check
```

Exit `0` means the vendored content and the Aspire catalog URL match the
recorded pins. Exit `1` lists the drifting packs with the recorded and actual
values; decide whether the drift is expected before updating.

## 2. Update

```bash
# All packs, using the refs recorded in sources.json (default mode)
bash .opencode/skills/sync-agent-skills.sh --update

# One pack only
bash .opencode/skills/sync-agent-skills.sh --pack duende --update

# Move one pack to the current head of its main branch
bash .opencode/skills/sync-agent-skills.sh --pack dotnet --latest

# Pin an explicit revision
bash .opencode/skills/sync-agent-skills.sh --pack duende --ref duende=<40-char sha>
```

`--update` downloads the `codeload.github.com` tarball at the recorded ref,
stages the included upstream directories, replaces only the managed pack
directory (`.opencode/skills/duende` or `.opencode/skills/dotnet`), copies the
upstream `LICENSE`, recomputes the content hash in `sources.json`, and rewrites
the pinned Aspire catalog URL in `.opencode/opencode.json`. The Aspire pack is
not vendored; updating it re-pins the URL and validates the catalog index.

## 3. Review the diff

- Inspect `git diff --stat` plus the changes under `.opencode/skills/duende`,
  `.opencode/skills/dotnet`, `.opencode/skills/sources.json`, and
  `.opencode/opencode.json`.
- Look for renamed, removed, or newly added skills. Update
  `.opencode/skills/README.md` when the included/excluded tables, counts, or
  pinned refs change.
- Confirm the expected counts: 26 Duende skills, 38 .NET skills across the four
  curated plugins (`dotnet` 5, `dotnet-aspnetcore` 2, `dotnet-diag` 7,
  `dotnet-test` 24), and 8 Aspire catalog entries.
- Check for duplicate skill IDs:

  ```bash
  find .opencode/skills -name SKILL.md -printf '%h\n' | xargs -n1 basename | sort | uniq -d
  ```

  It must print nothing.
- Confirm every ref in `README.md` equals the corresponding ref in
  `sources.json`, and that the Aspire catalog is the only non-vendored pack.

## 4. Verify and commit

```bash
bash .opencode/skills/sync-agent-skills.sh --check
git status --short
```

Commit the refreshed pack content, `sources.json`, the `opencode.json` URL
rewrite, and `README.md` together. Do not commit temporary downloads or local
skill caches.

# Nymeria Git Guidelines

These rules apply to every change in the Nymeria repository. They exist to keep
history small, reviewable, and truthful. See
[ARCHITECTURE.md](ARCHITECTURE.md) for project scope and governance.

## Core workflow

1. Inspect the repository and read the documentation before changing anything.
2. Make one small, logically complete change.
3. Verify it locally.
4. Inspect `git status` and the diff.
5. Stage only the intended files.
6. Commit immediately with a descriptive imperative message.
7. Push to the verified Nymeria remote.
8. Check the actual GitHub Actions result and report its conclusion.

Do not bundle unrelated changes into one commit. Do not leave a verified change
uncommitted while starting unrelated work.

## Before every commit

Run and review:

```bash
git status
git diff
git diff --staged
```

Confirm that:

- Only intended files are staged.
- No secrets, credentials, tokens, or machine-specific configuration are
  included.
- No build artifacts, caches, logs, or generated files are included.
- The change is complete and verified.

Prefer staging explicit paths, for example `git add docs/ARCHITECTURE.md`, so
unrelated work is not swept into the commit.

## Commit messages

Use descriptive, imperative messages. A short type prefix is encouraged:

- `chore: add repository structure`
- `ci: validate repository metadata`
- `docs: define git workflow`
- `fix: correct broken documentation link`
- `feat: add ...` only for actual functionality
- `refactor: ...`, `test: ...` when applicable

Keep the subject concise. Add a body when the reason or trade-off is not obvious.
Do not use messages such as `update`, `changes`, or `wip` on their own.

## Verification and failed local changes

Verify before committing. Verification may include YAML parsing, file and link
checks, configuration inspection, and the repository's GitHub Actions checks.

If verification fails:

- Undo only the failing uncommitted change.
- Preserve unrelated work.
- Fix the change and verify again before committing.

Do not use `git reset --hard`, `git clean -fd`, force-push, or delete unrelated
files without explicit maintainer approval. Those commands can destroy work that
is not part of the current change.

## Pushing

Push each verified, logically complete commit promptly:

```bash
git push origin <branch>
```

A successful local commit is **not** proof of a successful push. Confirm the
push result and the remote branch state, for example:

```bash
git status --short --branch
git ls-remote origin refs/heads/<branch>
```

A successful push is **not** proof that GitHub Actions passed. The workflow
result is a separate fact.

## Checking GitHub Actions

After pushing, inspect the run for the pushed commit:

```bash
gh run list --branch <branch> --limit 5
gh run view <run-id>
gh run view <run-id> --log-failed
```

Report the run link and its actual conclusion: success, failure, cancelled, or
pending. If the workflow is still running, report it as **pending**, never as
successful. Do not claim a check passed without seeing the conclusion.

## Failed push

- **Rejected as non-fast-forward:** fetch first (`git fetch origin`), inspect the
  divergence, and rebase or merge as appropriate. Never force-push over published
  history.
- **Authentication or authorization failure:** correct the credentials or access
  locally. Never paste secrets into the repository, logs, or chat.
- **Network or service failure:** retry after confirming the remote is reachable.
- If the push cannot be completed, report the blocker and the exact state of the
  local commit. Do not describe it as pushed.

## Failed CI run

If the `Repository checks` run fails:

1. Read the failing step and its logs.
2. Reproduce or reason about the failure locally where possible.
3. Fix the cause and push a new commit.
4. Re-check the new run and report its conclusion.

If the failing change should not be kept, revert it with a **new commit**:

```bash
git revert <commit-sha>
git push origin <branch>
```

If the failure is transient infrastructure, re-run the job and report the actual
result. Do not rewrite published history to hide a failure.

## Faulty published change

If a faulty change was already pushed, do not rewrite published history. Publish
a corrective revert commit instead:

```bash
git revert <commit-sha>
git commit -m "revert: <reason>"
git push origin <branch>
```

Then confirm the push and the resulting CI run. A follow-up fix commit is also
acceptable when it is clearer and keeps history truthful.

## Secrets and local files

Never commit:

- Passwords, tokens, private keys, certificates, or connection strings.
- `.env` files or other local environment configuration.
- Build output, caches, coverage output, logs, or generated artifacts.
- Editor or operating-system configuration specific to one machine.

If a secret is committed, treat it as compromised, rotate it, and request
maintainer guidance. Removing it from the latest commit is not enough.

## Branches, pull requests, and protected branches

- Do not silently create branches or open, merge, or close pull requests.
- Request maintainer approval before changing branch strategy or repository
  workflow.
- The only approved automatic merge path is
  `.github/workflows/dependabot-auto-merge.yml`. It may merge only pull requests
  authored by `dependabot[bot]`, targeting the default branch, after
  `Repository checks` succeeds on the current head commit. Do not extend
  auto-merge to other authors or workflows without approval.
- The organization currently blocks GitHub Actions from creating or approving
  pull requests. Do not add a personal access token, GitHub App credential, or
  other secret to automate approvals without explicit approval.
- Branch protection or GitHub rulesets are not enabled by default. Enabling them
  requires approval because it may conflict with the immediate-push workflow.
- Human-authored pull requests are reviewed and merged manually.

## Prohibited operations

Without explicit maintainer approval, do not:

- Force-push (`git push --force` or `--force-with-lease`).
- Rewrite published history (rebase, amend, or squash commits already pushed).
- Run `git reset --hard` or `git clean -fd`.
- Delete unrelated files, branches, or history.
- Change the remote, default branch, repository visibility, or ownership.
- Add secrets, credentials, or unapproved dependencies.

## Reporting

When reporting work, state facts that were actually verified:

- Commit hashes.
- Remote URL and branch.
- Push outcome.
- GitHub Actions run link and conclusion.
- Blockers or decisions that need maintainer approval.

Never claim a push or a check succeeded without confirming its actual result.

## Git Commit Attribution

- Do not add AI agents as commit authors or committers.
- Do not add YOURSELF as commit authors or committers.
- Do not add `Co-authored-by` trailers identifying AI tools.
- Preserve the repository's configured Git author identity.
- Never modify Git author or committer configuration.
- Never rewrite Git history without explicit approval.

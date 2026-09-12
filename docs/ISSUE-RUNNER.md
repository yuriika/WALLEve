# Issue Runner

This is the reusable operating contract for an implementation model. The GitHub issue supplies the feature-specific scope, acceptance criteria, and tests; do not repeat them in a prompt.

## Minimal assignment prompt

```text
Repository: /Users/yuri/source/walleve
Implement only Issue #<NUMBER> following docs/ISSUE-RUNNER.md.
```

## Required procedure

1. Read `CONTRIBUTING.md`, `DEVELOPMENT.md`, `docs/ISSUE-ORDER.md`, and the complete GitHub issue (including comments).
2. Run `git fetch origin`. Confirm that the worktree is clean and that every direct predecessor is contained in `origin/dev`. An open PR is not a predecessor; an integrated commit is.
3. If the issue is a tracking issue, a predecessor is absent, the worktree contains unrelated changes, or an acceptance criterion is genuinely unclear, report the blocker and stop without changing code.
4. Create `fix/<NUMBER>-<slug>` or `feat/<NUMBER>-<slug>` from current `origin/dev`.
5. Verify the issue's claim against current code. Implement the smallest complete change; add or adjust tests when behavior, state, persistence, calculations, or error handling changes.
6. Before committing, run the issue-specific checks plus:
   - `dotnet build`
   - `dotnet test tests/WALLEve.Tests/WALLEve.Tests.csproj --no-restore`
   - `git diff --check`
   Review the complete diff against `origin/dev`. Do not hide warnings, delete tests, invent domain rules, or include secrets/local data.
7. Stage only task files, commit, and push only the issue branch. Never push directly to `dev` or `master`, force-push, merge, enable auto-merge, or close the issue. After successful review, verification, and merge, the authorized reviewer closes the issue when all acceptance criteria are complete.
8. Open one PR explicitly targeting `dev`. Use `Refs #<NUMBER>`, describe changes, non-goals, acceptance-criterion evidence, actual test output, and remaining risks/warnings.
9. Read back the PR's base, head, commit, diff, and checks. No reported CI checks is not a successful CI run. Stop after reporting the PR link, branch, commit, verification, and blockers.

## Reviewer handoff

A separate reviewer verifies the remote PR, full diff, tests, checks, and scope. Only the authorized reviewer may merge a successful PR into `dev` after an explicit merge request from the maintainer. Because `dev` is not the GitHub default branch, use `Refs #<NUMBER>`; after merge, the reviewer closes the issue when all acceptance criteria are complete.

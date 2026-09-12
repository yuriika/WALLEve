# WALL-EVE Agent Instructions

## Repository workflow

- Read `CONTRIBUTING.md`, `DEVELOPMENT.md`, `docs/ISSUE-RUNNER.md`, and `docs/ISSUE-ORDER.md` before changing code.
- Implement exactly one executable GitHub issue per branch and pull request.
- Tracking issues are umbrellas; do not implement them as one PR.
- Start only from freshly fetched `origin/dev`.
- Check direct predecessors in `docs/ISSUE-ORDER.md` and verify that their commits are integrated in `origin/dev`.
- If the worktree contains unrelated changes, do not discard, stash, or adopt them; report a blocker.

## Branches and pull requests

- Use `fix/<issue-number>-<slug>` or `feat/<issue-number>-<slug>`.
- Never push directly to `dev` or `master`.
- Never force-push.
- Every executable issue gets one PR explicitly targeting `dev`.
- Use `Refs #N` in PR descriptions; `dev` is not the repository default branch.
- Implementation models must not merge, enable auto-merge, or close issues. After a successful review, complete verification, and merge, the authorized reviewer closes the issue when all acceptance criteria are fulfilled.

## Implementation

- Verify the issue's claims against the current code before editing.
- Implement all acceptance criteria; do not silently reduce scope.
- Keep changes minimal and task-related.
- Do not invent APIs, domain rules, or successful verification results.
- Preserve user data and manual values.
- Use additive database migrations and test migration safety when schema changes are required.
- Never commit secrets, tokens, credentials, local databases, or private configuration.
- Keep German and English README sections equivalent when documentation changes affect both.

## Verification

Before committing, run the issue-specific checks and:

```bash
dotnet build
dotnet test tests/WALLEve.Tests/WALLEve.Tests.csproj --no-restore
git diff --check
```

- Add regression tests for calculations, state transitions, persistence, filtering, and error handling.
- For pure Razor compilation or layout-only fixes, document why a unit test is not appropriate.
- Review the complete diff against `origin/dev`.
- Report existing warnings separately from new warnings; never suppress warnings globally.
- Read back the remote PR's base, head, commit, diff, and checks before reporting it ready.
- No reported CI checks is not the same as a successful CI run.

## Review handoff

After a PR is opened, an authorized external reviewer checks scope, correctness, tests, warnings, security, and acceptance criteria. Review corrections stay on the same issue branch and are re-tested. After a successful review and explicit maintainer authorization, the authorized reviewer may merge into `dev` and then update `docs/ISSUE-ORDER.md` with the integrated and newly ready issues.

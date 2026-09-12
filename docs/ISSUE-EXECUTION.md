# Issue execution

`docs/ISSUE-RUNNER.md` is the canonical reusable contract for a model implementing one issue. The complete GitHub issue supplies the task-specific scope, acceptance criteria, and verification; do not copy them into an assignment prompt.

`docs/ISSUE-ORDER.md` is the canonical dependency graph. It selects only issues whose predecessors are integrated in `origin/dev` and prevents later milestones from starting early.

## Assignment

```text
Repository: /Users/yuri/source/walleve
Implement only Issue #<NUMBER> following docs/ISSUE-RUNNER.md.
```

For an unassigned worker, replace `Issue #<NUMBER>` with `the next eligible issue from docs/ISSUE-ORDER.md`.

## Ownership

- The assigned model implements one executable issue on one branch and opens one PR to `dev`.
- It does not merge, enable auto-merge, push directly to `dev`/`master`, or close the issue. After successful review, verification, and merge, the authorized reviewer closes the issue when all acceptance criteria are complete.
- A separate authorized reviewer verifies the PR and may merge it after an explicit maintainer request.
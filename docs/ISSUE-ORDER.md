# Issue Execution Order

GitHub issues are the authoritative contracts. This document is the versioned dependency graph used to select the next executable issue. A direct predecessor must be integrated in `origin/dev`; an open PR is never sufficient.

## Selection rules

1. Finish all executable issues in an earlier milestone before starting a later milestone.
2. Within a milestone, select an issue only after all IDs to its left of `←` are integrated in `origin/dev`.
3. Issues on separate branches of the graph may proceed independently, but each model receives one issue and one PR only.
4. If the graph and a current GitHub issue disagree, stop and report the discrepancy; do not silently change the sequence.
5. After each successful review and merge, the reviewer updates the `Integrated` and `Ready now` status below in the same documentation PR or merge follow-up before assigning another issue.

## M0 — Data Trust

```text
#2  → #23
#24 → #25 → #26 → #27 → #28 → #29 → #30
                                  └──────→ #31
#4  → #32 → #33
```

**Integrated:** `#2`, `#23`, `#24`, `#25`, `#26`, `#27`, `#28`, `#29`, `#30`, `#31`, `#4`, `#32`, `#33`, `#35`, `#36`, `#37`, `#41`, `#42`, `#43`, `#44`, `#45`, `#46`, `#50`, `#51`, `#52`, `#53`, `#54`, `#57`, `#58`, `#59`, `#60`, `#63`.

**Ready now:** `#61`. Re-evaluate this list from freshly fetched `origin/dev` after every merge.

## M1 — Holdings Ledger

Starts only after M0 is complete.

```text
#35 → #41 → #50 → #57 ┐
              └──────→ #58 ┴→ #63
       └────→ #42 ──────────┐
#41 ─────────────────────────┴→ #52
#41 → #51
```

## M2 — Stockpiles

Starts only after M1 is complete.

```text
#36 → #43
#36 + #43 → #53 → #59
```

## M3 — Trading Console

Starts only after M2 is complete.

```text
#37 → #44 → #54 ─────────────┬→ #60 ─┐
       #45 ───────────────────┘        │
       #46 ────────────────┐            │
#54 + #46 → #61 → #64 ─────┴→ #66 → #67 → #69 → #71 → #72 → #73 → #74
                                  └→ #68 → #70
```

## M4 — Portfolio

Starts only after M3 is complete.

```text
#38 → #47
```

## M5 — Mining

Starts only after M4 is complete.

```text
#39 → #48
```

## M6 — Industry

Starts only after M5 is complete.

```text
#40 → #49 → #55 ┐
              └→ #56 ┴→ #62 → #65
```

## Maintenance

When creating, splitting, or reprioritizing an issue, update its direct-predecessor section on GitHub and this graph in the same PR. After a review/merge, update the integrated and ready status before assigning the next issue. Do not add a global numeric order: it would serialize independent work and create artificial blockers.

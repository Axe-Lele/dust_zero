---
name: dustzero-guide
description: Read the DustZero production guide, reconcile baseline versus target requirements, inspect existing implementation, and select the next milestone task without resetting the project.
argument-hint: "[feature or milestone]"
user-invocable: true
---

# DustZero guide and implementation checkpoint

Read `docs/design/README.md`, `SOURCE-DECISIONS.md` and `MILESTONES.md` in that directory.
Read `NEW-BUILD.md` first: the user supplied 10.15-h as the current development reference.
Use the relevant GAME-SPEC, IMPLEMENTATION or ART-AUDIO section for $ARGUMENTS.
If numerical detail is absent, consult the preserved source and actual tables; do not invent it.

Inspect existing files and evidence before selecting a task. Distinguish migration from the old 10.14-d copy,
10.15-h parity, guide art/architecture targets and release integration. Report what exists versus what is verified.
Do not treat missing target tables as empty data or mark newer features complete because an older variant exists.

Produce a short Korean checkpoint: selected version/scope, implementation evidence, gaps/conflicts,
next smallest task, required tests/screens, and any concrete missing data or decision.
Status values: NOT_ASSESSED, TODO, IN_PROGRESS, VERIFIED, BLOCKED.
VERIFIED requires real test/play evidence. No execution evidence means NOT_ASSESSED, not VERIFIED.
If asked only to review or organize the guide, stop after that deliverable; do not start game implementation.
If implementation was authorized, continue the selected task and apply `/game-quality` before reporting completion.
Follow the project's autonomous agreement for routine work and preserve existing game files.

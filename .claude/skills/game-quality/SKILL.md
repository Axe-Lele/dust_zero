---
name: game-quality
description: Validate a DustZero Unity feature through compilation, relevant tests, real play, original web comparison, and recorded evidence. Use after completing gameplay or UI changes and before claiming a feature is done.
argument-hint: "[feature or milestone]"
user-invocable: true
---

# Clean Girls game quality gate

Read `docs/qa/GAME-QUALITY.md`. The scope is $ARGUMENTS, or the feature just changed.
Read `docs/design/NEW-BUILD.md` and `SOURCE-DECISIONS.md` before choosing the reference version.
The current requested reference is 10.15-h; the older GameData copy must not silently substitute for it.
Follow the existing project automation agreement; routine checks do not require a new planning approval.

1. Inspect actual changes, `DustZeroUnity/ProjectSettings/ProjectVersion.txt`, package versions,
   existing tests and scenes. Do not infer current progress from an old checkpoint.
2. Establish scope, acceptance conditions and affected risks before running checks.
   Use `docs/qa/REPORT-TEMPLATE.md` to create a fresh report under
   `production/qa/evidence/<YYYYMMDD-HHMMSS>-<feature>/report.md`.
3. If Unity is open, confirm the MCP project path is this project's `DustZeroUnity`.
   Wait for import/compile and use Unity MCP to run relevant tests and query their final results.
   Inspect available tool schemas first; never invent tool names or parameters.
   If Unity is closed, use existing `tools/ccgs-unity.sh` compile/test commands.
   Do not launch a second editor on the same project or close someone else's editor.
4. Fix failures in the changed scope, then rerun the affected checks. No tests, absent results,
   a job still running, or editor startup alone is not a test pass.
5. Execute the changed gameplay/UI in Unity Play Mode. Verify the required transitions and
   inspect the rendered screen, not just the hierarchy or source code. Capture evidence.
6. For original behavior/visual parity, serve the selected version as described in NEW-BUILD.md and use Playwright
   on the returned localhost URL. Compare equivalent states, progression and viewport sizes.
   Use screenshots and vision coordinates for Canvas content that has no DOM controls.
   The original and isolated test saves are references; never erase the user's actual save.
7. Apply the relevant checklist from GAME-QUALITY.md. Reuse existing meaningful tests;
   add tests for uncovered gameplay regressions, not for each trivial UI edit.
   Use Context7 when uncertain about APIs, selecting installed package versions and official sources.
8. Record PASS / FAIL / UNVERIFIED / NOT_APPLICABLE per check, with concrete evidence.
   Every PASS must name a result, observation, screenshot or measurement from this run.
   NOT_APPLICABLE requires a scope reason. Missing required evidence is UNVERIFIED.
9. Report what works, what fails, and what is still unverified in Korean.
   A required FAIL means FAIL overall; otherwise a required UNVERIFIED means UNVERIFIED overall.
   Do not declare the game finished when only the scoped feature passed.

Keep Unity operations serial. Stop only the local comparison server/browser started by this run.
Do not install more harnesses, publish, commit, or change engine versions as part of this gate.

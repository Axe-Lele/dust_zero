# CCGS installation — 2026-10-15

- Upstream: https://github.com/Donchitos/Claude-Code-Game-Studios
- Snapshot: `be8993bbc5a1f016bc770b2846ce06272d284526` (framework 1.1.3).
- Imported the tracked harness, skills, agents, hooks and reference documentation.
- Upstream CI workflows were not imported; they test the framework, not this game.
- Existing game inputs and tools were preserved. The project was not re-created.
- Project-specific changes: CLAUDE.md, project.yaml, Unity reference, technical preferences,
  nested Unity path rules/helper, data validation paths, checkpoint, brief, local Bash configuration.
- Short-document mode with standard QA evidence. Routine edits proceed within the agreed scope.
- Upstream file-by-file approval prose is overridden by this project's documented automation setting.
- `acceptEdits` is local to this workspace; shell permissions and upstream deny rules remain in effect.
- Start Claude from `C:/Users/Master/Documents/Project/clean-girls`, not DustZeroUnity.
- `Start-Claude.ps1` sets the working folder, Git Bash and Python paths then starts the installed CLI.
- `Start-Claude.cmd` provides the same setup and can be double-clicked in Explorer.
- Restart an already-open Claude session to load the new project settings and skills.
- Unity MCP v10.3.0 was subsequently installed and its read-only Unity connection verified; see UNITY-MCP.md.
- Context7, Playwright MCP and the project /game-quality procedure were added; see QUALITY-TOOLS.md.
- The user production guide is integrated under docs/design; /dustzero-guide reads the milestone mapping.
- The new external reference build is 10.15-h; see docs/design/NEW-BUILD.md. GameData remains the older copy until an explicit versioned import.
- The MCP editor assembly compiled and the editor opened during integration validation. Gameplay/tests were NOT run.
- Build/run entrypoints explicitly report unimplemented until game scenes and a player build exist.
- Git metadata, if initialized here, belongs to the game; the harness snapshot is vendored,
  not configured as the game's origin. Keep LICENSE.CCGS with redistributed harness files.

## Resume prompt

CLAUDE.md와 production/session-state/active.md를 읽고 기존 Unity 이식 작업을 이어가줘.
현재 구현 상태부터 확인하고 첫 스테이지의 배치→전투→결과→로비 복귀를 완성해줘.

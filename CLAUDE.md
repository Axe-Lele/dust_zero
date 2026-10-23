# Clean Girls / Dust Zero — Unity port

사용자에게는 한국어로 간결하게 설명한다. 이 프로젝트는 기존 웹 게임의 Unity 이식 작업이다.
Claude Code Game Studios(CCGS)가 이미 적용되어 있다. 새 게임 기획이나 /start 온보딩부터 다시 시작하지 않는다.

## Read first

@docs/DUSTZERO-PORT-BRIEF.md
@docs/engine-reference/unity/VERSION.md
@docs/design/README.md
@docs/design/NEW-BUILD.md

`production/session-state/active.md`를 읽고 실제 파일 상태와 대조하여 이어간다.
`project.yaml`이 하네스 설정의 원본이다. 사용자 지시와 이 프로젝트의 요구사항이 일반 템플릿보다 우선한다.

상류 웹 빌드에서 가져온 파일(`GameData/tools/*.py`, `tables/*.xlsx`, 스프라이트 내보내기 등)은
버전을 소스에 박지 않고 `GameData/ACTIVE-VERSION.txt`를 거친다. 새 원본을 반영하는 작업은
`python tools/check_upstream_drift.py`가 통과해야 끝난다. 까닭은 `.claude/rules/dustzero-project.md`.

## Actual workspace layout

- Claude 작업 루트: `C:/Users/Master/Documents/Project/clean-girls`
- Unity 프로젝트: `DustZeroUnity/` (Unity Hub에서는 이 폴더를 연다)
- 게임 코드: `DustZeroUnity/Assets/DustZero/Scripts/`
- 씬/프리팹/그림: `DustZeroUnity/Assets/DustZero/` 아래에 역할별 구성
- 테스트: `DustZeroUnity/Assets/Tests/` (EditMode, PlayMode와 asmdef)
- 웹 원본 사본: `GameData/original_index.html`, `GameData/original_README.txt`
- 밸런스 원본: `GameData/tables/`; 변환기: `GameData/tools/build.py`; 결과: `GameData/data/`
- 기존 Unity 실행 도구: `tools/unity.sh`; 하네스 진입 도구: `tools/ccgs-unity.sh`
- 개발 기록: `production/`; 실행 증거: `production/qa/evidence/`

템플릿의 `src/`, 루트 `Assets/` 예시는 이 프로젝트의 게임 코드 위치가 아니다.
기존 코드/Unity 설정/데이터/사용자의 진행 작업을 조사한 뒤 확장한다. 프로젝트를 다시 만들거나 기존 파일을 초기화하지 않는다.

## Working agreement

- `modes.rigor: minimal`, `qa.level: standard`, `modes.automation: autonomous`이다.
- 합의된 이식 범위의 코드 작성, 파일 생성, 수정, 테스트는 매번 확인하지 않고 진행한다.
- 템플릿의 매 파일 쓰기 승인, Question→Options→Approval 절차는 이 프로젝트에서는 위 automation 설정으로 대체한다.
- 요구사항 축소/게임 규칙 변경, 기존 자료 삭제, 엔진 버전 변경, 유료 외부 서비스 사용 등 범위가 달라지는 결정은 사용자에게 확인한다.
- Git 커밋·푸시·배포는 명시적으로 요청받았을 때 수행한다.
- 문서 작성만으로 작업을 완료하지 않는다. 기능 단위로 구현→컴파일→필요한 테스트→실행 화면 확인을 반복한다.
- UI는 uGUI + TextMeshPro다. Unity UI specialist의 일반적인 UI Toolkit 권고보다 이 선택이 우선한다.
- ECS/DOTS, DI 프레임워크, 새 길찾기 패키지는 현재 필요하지 않다. Addressables는 가이드의 출시 전 단계로 남긴다.
- 가전 Spine은 M6 아트 목표이며 초기 SpriteRenderer 구현과 구분한다. 단계별 기준은 docs/design 문서를 따른다.
- 여러 에이전트 사용 시 서로 다른 파일을 맡기고, Unity 에디터 조작 및 컴파일/테스트는 한 작업자만 직렬로 수행한다.
- 테스트 실패·실행 불가·그림 자산 임시 상태를 숨기지 않는다. 미실행은 미확인으로 기록한다.

## Unity MCP

CoplayDev/unity-mcp v10.3.0이 설치되어 있다. 연결 설정은 루트 `.mcp.json`의 `unityMCP`다.
Unity Hub에서 `DustZeroUnity/`를 열면 프로젝트 전용 stdio 브리지가 자동 시작된다.
Claude를 루트에서 새로 실행하고 최초 MCP 사용 승인 후 이용한다. 먼저 프로젝트 정보로 대상 경로를 확인한다.
씬/프리팹/에디터/콘솔 조작은 연결된 Unity MCP를 활용하고, 컴파일·에셋 임포트 중에는 완료를 기다린다.
Unity가 닫혔거나 MCP 도구가 노출되지 않으면 연결 상태를 설명하고 파일 작업만 진행한다. 연결 성공을 추측하지 않는다.
설정·복구·검증 범위는 `docs/harness/UNITY-MCP.md`에 기록되어 있다.

## Quality and reference tools

기능 또는 UI 구현을 마쳤을 때, 완료라고 보고하기 전에 `.claude/skills/game-quality/SKILL.md`의
게임 검증 절차를 적용한다. 사용자가 `/game-quality <기능>`으로 직접 실행할 수도 있다.
검사 기준은 `docs/qa/GAME-QUALITY.md`, 보고서 양식은 `docs/qa/REPORT-TEMPLATE.md`다.
실제 플레이·캡처·관련 테스트 증거를 남기고 미실행 항목을 통과로 처리하지 않는다.

- `context7` MCP: API가 불명확하거나 라이브러리 사용 코드를 작성할 때 설치 버전의 문서를 조회한다.
  공식 출처를 우선하고 지원되지 않는 버전은 공식 Unity 문서로 확인한다. 비밀값이나 프로젝트 전체를 질의에 넣지 않는다.
- `playwright` MCP: `python tools/serve-original.py`로 연 원본 사본을 조작·촬영해 Unity와 비교한다.
  Canvas에는 screenshot/vision 도구를 사용한다. 네이티브 Unity 조작은 Unity MCP가 담당한다.
- 두 서버의 설정/복구/검증 기록은 `docs/harness/QUALITY-TOOLS.md`를 참고한다.

## CCGS reference files

@.claude/docs/coding-standards.md

필요할 때 `.claude/docs/coordination-rules.md`와 `.claude/docs/context-management.md`를 읽는다.
위 프로젝트별 경로와 작업 합의가 일반 문서의 예시/기본값보다 우선한다.
사용 가능한 명령에는 `/project-stage-detect`, `/create-stories`, `/dev-story`, `/code-review`, `/smoke-check`가 있다.

## First action

현재 코드/씬/테스트 상태와 신규 원본 10.15-h를 대조한다. 내부 GameData가 여전히 10.14-d이면 최신 원본으로 착각하지 않는다.
`/dustzero-guide`로 docs/design/MILESTONES.md의 실제 미완성 단계를 고르고, 이미 된 구현을 보존하며 이어간다.
추가 개발 지시를 받은 경우 분석 문서만 쓰고 멈추지 말고 기능별 구현→실행→`/game-quality`로 검증한다.
이 파일 설치만으로 게임 컴파일 또는 테스트가 완료된 것은 아니다.

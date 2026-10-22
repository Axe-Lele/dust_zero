# 품질 검증 도구 설치 — 2026-10-15

## 추가된 구성

1. `.claude/skills/game-quality/SKILL.md`: `/game-quality <기능>` 명령과 기능 완료 시 검증 절차.
   `CLAUDE.md`에서 기능 완료 전에 적용하도록 지시한다. 외부 강제 실행 서비스나 매 편집마다
   전체 테스트를 돌리는 훅이 아니라, Claude가 변경 범위에 맞춰 수행하는 절차다.
2. `context7`: 공식 원격 HTTP MCP `https://mcp.context7.com/mcp`.
   별도 키 없이 실제 조회 성공. 익명 사용량 제한이 생기면 공식 문서로 조회를 이어간다.
   키를 사용하려면 Context7 계정에서 발급하고 로컬 설정으로 관리하며 저장소에 넣지 않는다.
3. `playwright`: Microsoft `@playwright/mcp` 0.0.83, 로컬 Node 런타임과 설치된 Edge 사용.
   headless + isolated + vision, 기본 390×844. 개인 브라우저 로그인/저장을 사용하지 않는다.

기존 `unityMCP` 설정과 CCGS 구성을 보존했다. 게임 코드·밸런스·엔진 버전은 이 설치에서 변경하지 않았다.

## Claude에서 시작

- Claude CLI 또는 데스크톱 Code 탭의 Local 환경에서 `C:/Users/Master/Documents/Project/clean-girls`를 연다.
- 기존 세션을 재시작하고 표시되는 MCP 서버 사용 승인을 진행한다.
- Unity MCP는 Unity에서 `DustZeroUnity`가 열려 있어야 한다. Context7과 Playwright는 독립적으로 실행된다.
- `/game-quality 첫 스테이지` 또는 “기능 구현 후 game-quality 절차로 검증하고 증거를 남겨줘”라고 지시한다.
- 원본 비교는 `python tools/serve-original.py`를 실행하고 표시된 localhost URL로 접속한다.
  필요하면 기존 로컬 Python 또는 `build/unity-mcp-tools/Scripts/python.exe`로 실행할 수 있다.
- Playwright 기본 캡처 폴더: `production/qa/evidence/browser/`.
  기능 검증에서는 실행별 새 폴더를 사용해 과거 캡처와 구분한다.

## 확인한 결과

| 확인 | 결과 | 증거 |
| --- | --- | --- |
| Context7 초기화/도구 목록 | 성공, 도구 2개 | context7-validation.json |
| Unity 라이브러리 검색/공식 문서 내용 조회 | 성공 | context7-validation.json |
| Playwright MCP 초기화/vision 도구 등록 | 성공 | playwright-validation.json |
| 원본 게임 HTTP 제공/페이지 열기 | 성공, 게임 제목과 로비 데이터 확인 | playwright-validation.json |
| 원본 390×844 화면 캡처/시각 확인 | 성공 | 프로젝트 루트의 production/qa/evidence/browser/setup-original.png |
| 브라우저 콘솔 조회 | favicon.ico 404 1건; 게임 JavaScript 오류는 이 조회에서 관찰되지 않음 | playwright-validation.json |
| Claude 설정 인식 | 서버 3개 등록 확인, 최초 사용 승인 대기 | claude-mcp-registration.txt |

Context7 응답 버전은 조회 당시 4.4.1이며 원격 서비스 버전은 서버 측에서 변경될 수 있다.
실제 Unity 게임 플레이/게임 테스트/모바일 빌드는 이번 설치 검증에 포함되지 않았다.
검증용 원본 서버와 브라우저는 종료했다. 이 결과는 도구 연결 검증이며 게임 완성도 통과 보고서가 아니다.

## 재설치와 경로 변경

```powershell
powershell -ExecutionPolicy Bypass -File tools/Restore-BrowserMcp.ps1
```

위 명령은 `tools/mcp-runtime/package-lock.json`의 버전으로 복구한다.
Node와 Edge 설치가 필요하며 현재 Node 경로는 `C:/Program Files/nodejs/node.exe`다.
`.mcp.json`의 실행/출력 경로는 이 PC의 프로젝트 위치에 맞춰져 있으므로 폴더를 옮기면 갱신한다.
원격 Context7에는 로컬 패키지 설치가 필요 없다.
설정 수정 전 백업은 `build/quality-tools-backup-<시각>/`에 있다.

## 출처

- Context7: https://github.com/upstash/context7
- Context7 연결: https://context7.com/docs/resources/all-clients
- Playwright MCP: https://github.com/microsoft/playwright-mcp
- Claude CLI/데스크톱 설정 공유: https://code.claude.com/docs/en/desktop#shared-configuration

# Unity MCP 연결 — 2026-10-15

## 사용

1. Unity Hub에서 `C:/Users/Master/Documents/Project/clean-girls/DustZeroUnity`를 연다.
2. 임포트와 컴파일이 끝날 때까지 기다린다.
3. 루트의 `Start-Claude.cmd`로 새 Claude 세션을 시작한다.
4. 최초 표시되는 `unityMCP` 프로젝트 서버 사용 승인을 진행한다.
5. `/mcp`에서 상태를 확인하고, 프로젝트 정보를 읽어 `DustZeroUnity`인지 확인한다.

이미 실행 중인 Claude는 새 설정을 읽도록 재시작한다. Unity는 에디터 조작 중 열려 있어야 한다.
Claude에는 “Unity MCP로 현재 프로젝트와 콘솔을 확인한 뒤 CLAUDE.md에 따라 작업을 이어가줘”라고 지시하면 된다.

## 설치 구성

- 출처: https://github.com/CoplayDev/unity-mcp
- 고정 버전: v10.3.0, commit `aa5fc638d623f56178d50329d9ad8c541a57fe66`.
- Unity embedded package: `DustZeroUnity/Packages/com.coplaydev.unity-mcp`.
- Python 서버: `build/unity-mcp-tools`, mcpforunityserver 10.3.0.
- Claude 설정: `.mcp.json`, `.claude/settings.local.json`.
- 프로젝트 연결 코드: `DustZeroUnity/Assets/Editor/CleanGirlsMcp/ProjectMcpConnection.cs`.
- 통신: stdio 서버와 Unity의 로컬 브리지. 프로젝트 상태 폴더는 `build/unity-mcp-status`.
- 세션별 stdio 선택을 사용한다. 다른 프로젝트의 전역 transport 설정은 변경하지 않는다.
- 진단 전송은 `UNITY_MCP_DISABLE_TELEMETRY=1`로 비활성화했다.
- 기존 Unity 버전 6000.4.5f1과 게임 코드를 유지했다.
- 원본 라이선스는 패키지 `LICENSE.md`에 포함된다.

## 검증 결과와 한계

실제 Unity 에디터를 배치 모드로 열어 플러그인과 프로젝트 연결 코드가 컴파일됨을 확인했다.
MCP 서버 초기화, 도구 48개 열거, 대상 프로젝트 정보, 활성 씬 정보, 콘솔 읽기에 성공했다.
결과는 `unity-mcp-validation.json`, Unity 로그는 `build/unity-mcp-setup.log`에 있다.
서버 handshake의 version 3.4.8은 FastMCP 프레임워크 버전이며 설치 패키지는 10.3.0이다.

`claude mcp list`에서 프로젝트 서버 등록을 확인했으며 최초 사용 승인 대기 상태다.
사용자 Claude 세션 안에서의 도구 호출은 아직 수행하지 않았다.
배치 실행 중 URP의 기존 머티리얼 업그레이드 안내 Assert가 기록되었다.
에디터 상태 resource에는 stale_status가 반환되었지만 실제 프로젝트/씬/콘솔 조회는 성공했다.
검증에서 게임 실행, 씬 저장, 게임플레이 테스트는 수행하지 않았다.
검증용 에디터는 종료했으므로 사용 시 Unity 프로젝트를 직접 열어야 한다.

## 복구

`build/`는 Git에서 제외되므로 새 체크아웃이나 build 폴더 정리 후 다음을 실행한다.

```powershell
powershell -ExecutionPolicy Bypass -File tools/Restore-UnityMcp.ps1
```

런타임은 설치 당시의 패키지 버전을 `tools/unity-mcp-requirements.txt`에 기록한다.
프로젝트 위치를 옮겼다면 `.mcp.json`의 command와 UNITY_MCP_STATUS_DIR 절대 경로를 갱신한다.
Unity 컴파일 에러가 있으면 브리지가 시작되지 않을 수 있으므로 먼저 컴파일 에러를 해결한다.
설치 이전 설정 백업은 `build/mcp-setup-backup/`에 있다. 게임 진행 파일을 백업으로 일괄 덮어쓰지 않는다.

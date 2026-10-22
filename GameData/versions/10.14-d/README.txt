오퍼레이션: 더스트 제로 - 플레이 테스트 묶음
=============================================

[바로 플레이]
  index.html 을 더블클릭하면 브라우저(크롬 권장)에서 바로 실행됩니다.
  인터넷이 없어도 돌아가고, 연결돼 있으면 글꼴만 더 예쁘게 나옵니다.
  진행 데이터는 그 브라우저에만 저장됩니다. 처음부터 하려면 홈 맨 아래 "게임 초기화"(두 번 누름). 홈 맨 아래 빌드 번호로 최신 버전인지 확인할 수 있습니다.

[밸런스 수정 = 엑셀]
  1) tables/ 폴더의 엑셀을 열어 수정하고 저장 (엑셀을 닫지 않아도 됩니다)
  2) 명령창에서 이 폴더로 이동한 뒤:   python tools/build.py
     -> data/*.json 과 data/tables.js 가 새로 만들어집니다
  3) 브라우저 새로고침

  * 처음 한 번만:  pip install openpyxl   (파이썬 3 필요)
  * 스킬 설명 문구는 skill_param 수치로 게임이 자동으로 채웁니다.
    문구 자체를 바꾸려면 string_ko 시트에서 {pullRadius} 같은 자리표시자를 남겨 두고 고치세요.

[엑셀 규약]
  - 시트 이름 = 테이블 이름. 파일 이름은 정리용일 뿐 상관없습니다.
  - '#'으로 시작하는 시트는 변환에서 제외됩니다 (메모, 검산용으로 자유롭게 쓰세요).
  - 1행 = 컬럼 키(바꾸지 마세요), 2행 = 설명(무시됨), 3행부터 데이터.
  - A열 값 앞에 '#'을 붙이면 그 행은 빠집니다. 행을 지우지 않고 잠시 끌 때 쓰세요.
  - 배열 값은 [1, 6, 10] 처럼 대괄호로 적습니다 (config의 starLifeMin).
  - 맵 경로는 map_entrance.path 에 "0,2 > 1,2 > 1,8 > 3,8 > 3,9" 형식으로 적습니다.

[파일 구성]
  tables/01_Config.xlsx   config, enums, string_ko
  tables/02_Unit.xlsx     unit, skill_param, unit_levelup
  tables/03_Enemy.xlsx    enemy
  tables/04_Stage.xlsx    stage, wave, map, map_entrance, map_tile, star_reward, tutorial
  tables/05_Reward.xlsx   item, reward_group, condition, content_unlock
  tables/06_Gacha.xlsx    gacha, gacha_pool
  tables/07_Survivor.xlsx surv_param, surv_combo, surv_set, surv_wave (대청소 모드)
  data/                   엑셀에서 변환된 결과 (직접 고치지 마세요)
  tools/build.py          엑셀 -> 게임 데이터 변환
  tools/gen_tables.py     공식(HP 곡선, 시급 곡선 등)으로 엑셀을 처음부터 다시 생성
                          !! 실행하면 tables/*.xlsx 를 덮어씁니다. 엑셀에서 고친 내용이 사라지니 주의
  tools/xlsx_io.py, pack_tables.py   변환에 쓰는 보조 스크립트

[튜토리얼]
  스테이지가 아니라 '처음 하는 전투들'에 규칙을 하나씩 안내합니다. 전투 1판에 1묶음씩 순서대로 열리고,
  그 판에 못 본 안내는 다음 판으로 넘어갑니다. 보스·주방·욕실 안내는 그 전투를 처음 할 때 나옵니다.
  이미 진행한 저장 데이터도 다음 전투부터 처음 묶음이 나옵니다. 한 번 본 안내는 다시 안 뜹니다.
  다시 보려면 홈 맨 아래 "튜토리얼 다시 보기". 조건은 04_Stage.xlsx 의 tutorial 시트, 문구는 string_ko 의 tut.* 키.

[대청소 모드 (프로토타입)]
  홈의 "대청소 모드" 버튼. 1-1 클리어 후 열립니다(테스트 편의. content_unlock 의 survivor 행 cond 로 조정).
  리더(청소기·로봇청소기·에어컨)를 골라 5분 버티기. 화면을 끌거나 WASD·방향키로 이동, 공격은 자동.
  레벨업마다 편성의 가전이 동료로 합류(컴퓨터·선풍기·발전기는 리더에 붙음)하거나 강해집니다.
  협동기: 청소기+선풍기, 에어컨+냉장고, 로봇청소기+발전기 (리더 무기 Lv3 이상 + 해당 동료 최대 레벨).
  수치는 07_Survivor.xlsx 와 config 의 surv* 항목.

[테스트 팁]
  - 홈의 "테스트용 +1시간" 버튼으로 자동 청소 누적을 빨리 확인할 수 있습니다.
  - 전투 중 1×/2× 버튼으로 배속.

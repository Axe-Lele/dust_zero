# -*- coding: utf-8 -*-
"""메인 · 서브 퀘스트 원고 (10.22-a). gen_tables.py 가 읽어서 quest_line · quest 테이블과 string_ko 를 만든다.

메인 퀘스트 = 진행(메인 루프)을 따라가는 순차 체인. 한 번에 하나만 보이고, 받으면 다음 것이 열린다. 챕터(장)마다 묶음.
서브 퀘스트 = 콘텐츠별 줄기(대청소 · 협동 · 타워 · 리그 · 심화 · 호감도 · 장비 · 가전 키우기). 줄기마다 순차, 그 콘텐츠가 열리면 시작.
일일 · 주간 미션과 업적(10.21-c)은 그대로 따로 있다.

퀘스트 = (키, 목표, 이동, 문구, 보상)
  키: 상태(best·hbest·deck·own·max_lv·avg_lv·star_total·max_star·tower_best·coop_best·league_tier·surv_best·aff_max·story_n·mg_ok·equip_n·gear_max·excl_n·codex)
      또는 누적 횟수(track 키: pull·lvup·starup·gear_up·gear_pull·idle_claim·stage_3star·star_claim·surv_play·surv_min·coop_play·coop_wave·
      league_play·league_win·tower_clear·hard_clear·story·minigame·aff_talk·aff_gift·pool_pts·boss_clear·kill)
  이동: [이동] 버튼이 여는 곳(stage · hard · roster · deck · gacha · gear_gacha · gearbag · idle · surv · coop · tower · league · aff · story · stars · pool)
  보상: [("cry", n) | ("coin", n) | ("part", n) 부품 4종 각 | ("stone", n) | ("ess", n)]
  문구의 {n} = 목표 수(초는 '분 초'로 바꿔 씀). 비우면 키 기본 문구.
!! ID = 줄기 번호 × 1000 + 순서. 이미 받은 기록이 ID로 저장되니 중간에 끼워 넣지 말고 줄기 끝에 붙이세요.
"""

# 메인: (장 번호, 장 제목, 청소 본부 한마디, [퀘스트...])
MAIN = [
  (1, "첫 출동", "거실로 먼지가 몰려와요. 가전들과 첫 청소를 시작해요!", [
    ("best", 1, "stage", "1-1 거실 지키기", [("part", 3), ("cry", 20)]),
    ("lvup", 1, "roster", "가전 레벨 업 {n}번", [("part", 3)]),
    ("best", 2, "stage", "1-2 클리어", [("cry", 20)]),
    ("pull", 1, "gacha", "가전 소환 {n}번", [("cry", 30)]),
    ("deck", 5, "deck", "편성에 가전 {n}대 넣기", [("part", 5)]),
    ("idle_claim", 1, "idle", "자동 청소 보상 받기", [("part", 5)]),
    ("best", 3, "stage", "1-3 클리어", [("part", 3)]),
    ("stage_3star", 3, "stage", "별 3개로 {n}번 클리어", [("cry", 20)]),
    ("best", 5, "stage", "1-5 보스 물리치기", [("cry", 50), ("stone", 10)]),
  ]),
  (2, "대청소와 주방", "거실을 지켰어요! 이제 대청소 모드가 열렸고, 주방에서 기름때가 기다려요.", [
    ("surv_play", 1, "surv", "대청소 모드 한 판", [("cry", 30)]),
    ("max_lv", 5, "roster", "가전 하나 Lv.{n} 만들기", [("part", 5)]),
    ("star_claim", 1, "stars", "챕터 별 보상 받기", [("cry", 20)]),
    ("best", 8, "stage", "2-3 클리어", [("part", 5)]),
    ("pull", 10, "gacha", "가전 소환 누적 {n}번", [("cry", 50)]),
    ("deck", 6, "deck", "편성 {n}대 꽉 채우기", [("part", 10)]),
    ("best", 10, "stage", "2-5 보스 물리치기", [("cry", 50), ("stone", 10)]),
  ]),
  (3, "장비를 갖춰요", "욕실 곰팡이는 단단해요. 장비를 끼우면 가전이 훨씬 버텨요.", [
    ("gear_pull", 1, "gear_gacha", "장비 소환 {n}번", [("stone", 10)]),
    ("equip_n", 1, "gearbag", "가전에 장비 끼우기", [("stone", 10)]),
    ("gear_up", 3, "gearbag", "장비 강화 {n}번", [("stone", 10)]),
    ("best", 13, "stage", "3-3 클리어", [("part", 5)]),
    ("max_lv", 10, "roster", "가전 하나 Lv.{n} 만들기", [("part", 10)]),
    ("surv_best", 180, "surv", "대청소 {n} 버티기", [("cry", 30)]),
    ("best", 15, "stage", "3-5 보스 물리치기", [("cry", 50), ("stone", 15)]),
  ]),
  (4, "함께라서 든든해", "협동 청소가 열렸어요. 가전들과 더 가까워지는 법도 알려 드릴게요.", [
    ("coop_play", 1, "coop", "협동 청소 한 판", [("cry", 30)]),
    ("starup", 1, "roster", "가전 돌파 {n}번", [("part", 10)]),
    ("aff_talk", 1, "aff", "가전에게 말 걸기", [("part", 3)]),
    ("story", 1, "story", "가전 스토리 {n}화 보기", [("cry", 20)]),
    ("best", 18, "stage", "4-3 클리어", [("part", 5)]),
    ("avg_lv", 15, "roster", "편성 평균 Lv.{n}", [("part", 10)]),
    ("best", 20, "stage", "4-5 보스 물리치기", [("cry", 50), ("stone", 15)]),
  ]),
  (5, "세탁실 너머", "적이 단단해지기 시작해요. 돌파와 청소 일지를 챙겨요.", [
    ("coop_best", 10, "coop", "협동 청소 {n}웨이브 버티기", [("cry", 30)]),
    ("star_total", 5, "roster", "돌파 합 {n}", [("stone", 15)]),
    ("best", 23, "stage", "5-3 클리어", [("part", 5)]),
    ("pool_pts", 100, "pool", "청소 일지 누적 {n}점", [("cry", 30)]),
    ("best", 25, "stage", "5-5 보스 물리치기", [("cry", 50), ("stone", 15)]),
  ]),
  (6, "타워와 리그", "먼지 타워가 열렸어요. 6장을 넘으면 리그와 심화 모드도 기다려요.", [
    ("tower_best", 3, "tower", "먼지 타워 {n}층 오르기", [("cry", 30)]),
    ("avg_lv", 22, "roster", "편성 평균 Lv.{n}", [("part", 15)]),
    ("best", 28, "stage", "6-3 클리어", [("part", 5)]),
    ("best", 30, "stage", "6-5 보스 물리치기", [("cry", 50), ("stone", 20)]),
    ("league_play", 1, "league", "가전 대청소 리그 한 판", [("cry", 30)]),
    ("hbest", 1, "hard", "심화 1-1 클리어", [("cry", 30)]),
  ]),
  (7, "보일러실", "리그와 심화가 열렸어요. 메인 스테이지와 번갈아 하면 성장이 빨라요.", [
    ("tower_best", 8, "tower", "먼지 타워 {n}층 오르기", [("cry", 30)]),
    ("league_tier", 1, "league", "리그 브론즈 완주", [("cry", 30)]),
    ("best", 33, "stage", "7-3 클리어", [("part", 5)]),
    ("star_total", 10, "roster", "돌파 합 {n}", [("stone", 20)]),
    ("best", 35, "stage", "7-5 보스 물리치기", [("cry", 50), ("stone", 20)]),
  ]),
  (8, "창고의 나방", "날아다니는 적이 많아져요. '전체' 가전을 꼭 챙겨요.", [
    ("hbest", 5, "hard", "심화 1장 끝내기", [("cry", 30)]),
    ("avg_lv", 27, "roster", "편성 평균 Lv.{n}", [("part", 15)]),
    ("best", 38, "stage", "8-3 클리어", [("part", 5)]),
    ("aff_max", 4, "aff", "호감도 Lv.{n} 가전 만들기(스토리 2화)", [("cry", 30)]),
    ("best", 40, "stage", "8-5 보스 물리치기", [("cry", 50), ("stone", 20)]),
  ]),
  (9, "차고의 녹", "녹은 공격이 세요. 근접 가전을 나눠 세우고 장비를 키워요.", [
    ("tower_best", 12, "tower", "먼지 타워 {n}층 오르기", [("cry", 30)]),
    ("gear_max", 10, "gearbag", "장비 하나 +{n}", [("stone", 20)]),
    ("best", 43, "stage", "9-3 클리어", [("part", 5)]),
    ("league_tier", 2, "league", "리그 실버 완주", [("cry", 50)]),
    ("best", 45, "stage", "9-5 보스 물리치기", [("cry", 50), ("stone", 20)]),
  ]),
  (10, "다락방", "다락방 끝엔 보일러 조각이 있어요. 심화도 한 장씩 밀어 봐요.", [
    ("hbest", 10, "hard", "심화 2장 끝내기", [("cry", 30)]),
    ("avg_lv", 31, "roster", "편성 평균 Lv.{n}", [("part", 20)]),
    ("best", 48, "stage", "10-3 클리어", [("part", 5)]),
    ("own", 12, "gacha", "가전 {n}종 모으기", [("cry", 50)]),
    ("best", 50, "stage", "10-5 보스 물리치기", [("cry", 50), ("stone", 20)]),
  ]),
  (11, "베란다 곰팡이", "4구역이에요. 지금부터는 돌파가 레벨 상한을 열어 줘요.", [
    ("tower_best", 18, "tower", "먼지 타워 {n}층 오르기", [("cry", 30)]),
    ("star_total", 20, "roster", "돌파 합 {n}", [("stone", 20)]),
    ("best", 53, "stage", "11-3 클리어", [("part", 5)]),
    ("coop_best", 20, "coop", "협동 청소 {n}웨이브 버티기", [("cry", 30)]),
    ("best", 55, "stage", "11-5 보스 물리치기", [("cry", 50), ("stone", 20)]),
  ]),
  (12, "옥상 정원", "옥상까지 왔어요! 호감도가 높은 가전은 속마음을 들려줘요.", [
    ("hbest", 20, "hard", "심화 4장 끝내기", [("cry", 30)]),
    ("avg_lv", 35, "roster", "편성 평균 Lv.{n}", [("part", 20)]),
    ("best", 58, "stage", "12-3 클리어", [("part", 5)]),
    ("aff_max", 8, "aff", "호감도 Lv.{n} 가전 만들기(스토리 3화)", [("cry", 50)]),
    ("best", 60, "stage", "12-5 보스 물리치기", [("cry", 50), ("stone", 20)]),
  ]),
  (13, "환기 덕트", "먼지 왕국의 입구예요. 리그 골드에 도전할 때예요.", [
    ("tower_best", 22, "tower", "먼지 타워 {n}층 오르기", [("cry", 30)]),
    ("league_tier", 3, "league", "리그 골드 완주", [("cry", 80)]),
    ("best", 63, "stage", "13-3 클리어", [("part", 5)]),
    ("star_total", 30, "roster", "돌파 합 {n}", [("stone", 30)]),
    ("best", 65, "stage", "13-5 보스 물리치기", [("cry", 50), ("stone", 20)]),
  ]),
  (14, "하수 배관", "거의 다 왔어요. 장비를 끝까지 키워 봐요.", [
    ("hbest", 30, "hard", "심화 6장 끝내기", [("cry", 30)]),
    ("avg_lv", 38, "roster", "편성 평균 Lv.{n}", [("part", 20)]),
    ("best", 68, "stage", "14-3 클리어", [("part", 5)]),
    ("gear_max", 15, "gearbag", "장비 하나 +{n}", [("stone", 30)]),
    ("best", 70, "stage", "14-5 보스 물리치기", [("cry", 50), ("stone", 20)]),
  ]),
  (15, "먼지 왕좌", "모든 먼지의 황제가 기다려요. 집을 되찾아요!", [
    ("tower_best", 26, "tower", "먼지 타워 {n}층 오르기", [("cry", 30)]),
    ("hbest", 40, "hard", "심화 8장 끝내기", [("cry", 30)]),
    ("best", 73, "stage", "15-3 클리어", [("part", 5)]),
    ("avg_lv", 40, "roster", "편성 평균 Lv.{n}", [("part", 20)]),
    ("best", 75, "stage", "15-5 먼지 황제 물리치기", [("cry", 200), ("ess", 10)]),
  ]),
  (16, "끝나지 않는 대청소", "집은 되찾았지만 먼지는 또 와요. 심화와 타워 꼭대기를 정복해요.", [
    ("hbest", 50, "hard", "심화 10장 끝내기", [("cry", 50)]),
    ("tower_best", 30, "tower", "먼지 타워 {n}층 정복", [("cry", 100)]),
    ("own", 16, "gacha", "가전 {n}종 모두 모으기", [("cry", 100)]),
    ("hbest", 60, "hard", "심화 12장 끝내기", [("cry", 50)]),
    ("star_total", 50, "roster", "돌파 합 {n}", [("ess", 10)]),
    ("hbest", 75, "hard", "심화 15-5 정복", [("cry", 300), ("ess", 20)]),
  ]),
]

# 서브: (줄기 번호, 이름, 열림 조건, 설명, [퀘스트...])  열림: '' 바로 / 'unlock:<content_unlock id>' / 'best:N' / 'hard'
SUB = [
  (1, "대청소", "unlock:survivor", "혼자 무한 버티기. 오래 버틸수록 청소 일지도 빨리 차요.", [
    ("surv_play", 1, "surv", "대청소 한 판", [("cry", 20)]),
    ("surv_best", 120, "surv", "대청소 {n} 버티기", [("cry", 20)]),
    ("surv_min", 10, "surv", "대청소 누적 {n}분", [("stone", 10)]),
    ("surv_best", 240, "surv", "대청소 {n} 버티기", [("cry", 30)]),
    ("surv_min", 60, "surv", "대청소 누적 {n}분", [("stone", 20)]),
    ("surv_best", 360, "surv", "대청소 {n} 버티기", [("cry", 50)]),
    ("surv_min", 300, "surv", "대청소 누적 {n}분", [("ess", 5)]),
  ]),
  (2, "협동 청소", "unlock:coop", "파트너와 고리를 지켜요. 칸을 골라 소환하고 같은 ★끼리 합쳐요.", [
    ("coop_play", 1, "coop", "협동 청소 한 판", [("cry", 20)]),
    ("coop_best", 8, "coop", "{n}웨이브 버티기", [("cry", 20)]),
    ("coop_wave", 50, "coop", "누적 {n}웨이브", [("stone", 10)]),
    ("coop_best", 15, "coop", "{n}웨이브 버티기", [("cry", 30)]),
    ("coop_wave", 200, "coop", "누적 {n}웨이브", [("stone", 20)]),
    ("coop_best", 20, "coop", "{n}웨이브 버티기", [("cry", 50)]),
    ("coop_best", 25, "coop", "{n}웨이브 버티기", [("ess", 5)]),
  ]),
  (3, "먼지 타워", "unlock:tower", "층마다 규칙이 달라요. 한 번 깬 층은 주간 재청소로 다시 크리스탈을 줘요.", [
    ("tower_best", 2, "tower", "{n}층 오르기", [("cry", 20)]),
    ("tower_best", 5, "tower", "{n}층 오르기", [("cry", 20)]),
    ("tower_best", 10, "tower", "{n}층 오르기", [("cry", 30)]),
    ("tower_best", 15, "tower", "{n}층 오르기", [("cry", 30)]),
    ("tower_best", 20, "tower", "{n}층 오르기", [("cry", 50)]),
    ("tower_best", 25, "tower", "{n}층 오르기", [("cry", 50)]),
    ("tower_best", 30, "tower", "{n}층 정복", [("ess", 10)]),
  ]),
  (4, "가전 대청소 리그", "unlock:league", "상점에서 가전을 사서 합치는 오토배틀러. 단계마다 주간 완주 보상이 있어요.", [
    ("league_play", 1, "league", "리그 한 판", [("cry", 20)]),
    ("league_win", 10, "league", "리그 누적 {n}승", [("stone", 10)]),
    ("league_tier", 1, "league", "브론즈 완주", [("cry", 30)]),
    ("league_win", 50, "league", "리그 누적 {n}승", [("stone", 20)]),
    ("league_tier", 2, "league", "실버 완주", [("cry", 50)]),
    ("league_tier", 3, "league", "골드 완주", [("cry", 80)]),
    ("league_win", 200, "league", "리그 누적 {n}승", [("ess", 10)]),
  ]),
  (5, "심화 모드", "hard", "보통 스테이지에 규칙이 하나씩 붙어요. 챕터마다 보통 +5장을 깨야 열려요.", [
    ("hbest", 5, "hard", "심화 1장 끝내기", [("cry", 20)]),
    ("hbest", 10, "hard", "심화 2장 끝내기", [("cry", 20)]),
    ("hbest", 15, "hard", "심화 3장 끝내기", [("cry", 30)]),
    ("hbest", 25, "hard", "심화 5장 끝내기", [("cry", 30)]),
    ("hbest", 35, "hard", "심화 7장 끝내기", [("cry", 50)]),
    ("hbest", 50, "hard", "심화 10장 끝내기", [("cry", 50)]),
    ("hbest", 75, "hard", "심화 15장 끝내기", [("ess", 20)]),
  ]),
  (6, "가전과 친해지기", "best:2", "말을 걸고 간식을 주면 호감도가 올라요. 호감도가 오르면 스토리가 열려요.", [
    ("aff_talk", 1, "aff", "가전에게 말 걸기", [("cry", 10)]),
    ("aff_gift", 1, "aff", "간식 주기", [("cry", 10)]),
    ("story", 1, "story", "스토리 {n}화 보기", [("cry", 20)]),
    ("aff_max", 3, "aff", "호감도 Lv.{n} 가전 만들기", [("stone", 10)]),
    ("mg_ok", 1, "story", "미니게임 {n}종 성공", [("cry", 20)]),
    ("story", 6, "story", "스토리 누적 {n}화", [("cry", 30)]),
    ("aff_max", 6, "aff", "호감도 Lv.{n} 가전 만들기", [("stone", 20)]),
    ("mg_ok", 5, "story", "미니게임 {n}종 성공", [("cry", 30)]),
    ("aff_max", 10, "aff", "호감도 Lv.{n} 가전 만들기", [("ess", 5)]),
  ]),
  (7, "장비", "best:3", "공용 장비는 어느 가전에나, 전용 장비는 그 가전에만. 강화석으로 키워요.", [
    ("gear_pull", 1, "gear_gacha", "장비 소환 {n}번", [("stone", 10)]),
    ("equip_n", 3, "gearbag", "장비 낀 가전 {n}대", [("stone", 10)]),
    ("gear_up", 10, "gearbag", "장비 강화 누적 {n}번", [("stone", 20)]),
    ("excl_n", 1, "gearbag", "전용 장비 {n}개 얻기", [("cry", 30)]),
    ("gear_max", 10, "gearbag", "장비 하나 +{n}", [("stone", 20)]),
    ("equip_n", 6, "gearbag", "장비 낀 가전 {n}대", [("stone", 20)]),
    ("gear_max", 15, "gearbag", "장비 하나 +{n}(희귀 최대)", [("ess", 5)]),
  ]),
  (8, "가전 키우기", "", "레벨은 코인·부품, 돌파는 그 가전 조각으로 올려요. 돌파하면 레벨 상한이 열려요.", [
    ("max_lv", 10, "roster", "가전 하나 Lv.{n}", [("part", 5)]),
    ("star_total", 3, "roster", "돌파 합 {n}", [("stone", 10)]),
    ("max_lv", 20, "roster", "가전 하나 Lv.{n}", [("part", 10)]),
    ("own", 9, "gacha", "가전 {n}종 모으기", [("cry", 30)]),
    ("star_total", 15, "roster", "돌파 합 {n}", [("stone", 20)]),
    ("max_lv", 30, "roster", "가전 하나 Lv.{n}", [("part", 20)]),
    ("max_star", 5, "roster", "가전 하나 ★{n}", [("ess", 5)]),
    ("max_lv", 45, "roster", "가전 하나 Lv.{n}(최대)", [("ess", 10)]),
  ]),
]

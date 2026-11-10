# CardGame — 카드 익스트랙션 전투 게임

Unity 2022.3 LTS (2022.3.62f3), Universal 2D (URP). 모든 대화와 주석은 한국어.

## 작업 규칙 (모든 작업 전 필독)
- 작업 시작 전 Design/rules/작업규칙.md 와 Design/rules/실수기록.md 를 읽고 지킨다.
- 핵심: '작업해/진행해' 전엔 반영 금지, 지적은 승인이 아님, 답변은 한국어, 작업 단위마다 git 커밋, 지울 땐 확정 목록만 휴지통으로.
- 원본은 공유 폴더 rules/. 실수가 생기면 개발_작업규칙 스레드에 알린다.

## 역할
- 기획·그래픽: 프로젝트 소유자 (코딩 초보). 설명은 쉬운 말로, 단계별로.
- 코딩·구현: 협업 동료 + Claude.

## 규칙의 원본
- `Design/card_battle_rulebook_v0.7.3.xlsx` 가 **유일한 규칙 원본**이다. 규칙이 충돌하면 이 파일을 따른다.
- `Design/card_battle_sim_v0.5.xlsx` 는 옛 버전 시뮬레이터(참고용). 수치는 규칙집이 우선.
- 엑셀은 무거우므로 매번 읽지 않는다. 한 번 변환한 요약본을 쓴다:
  - `Design/rules.md` — 규칙 요약 (텍스트)
  - `Assets/Data/*.json` — 카드, 덱, 몬스터, 패턴, 토템 데이터
  - 규칙집이 새 버전으로 바뀌면 그때만 다시 변환한다.

## 핵심 규칙 (요약)
- 적이 5슬롯에 카드를 공개 배치 → 플레이어는 손패 7장(최대 9장) 중 최대 5장 배치.
- 1~5번 슬롯을 차례로 처리, 양쪽 동시 행동. 슬롯 순서: 이동 동시 계산 → 적중·피해·상태이상·소모품 정산 → 장판.
- 맵 7칸 일렬, 1·7번 칸은 벽. 위치는 라운드를 넘어 유지. 같은 칸 충돌 시 NPC 우선.
- 같은 슬롯 동시 사망 = 플레이어 패배.

## 코드 구조 원칙
- 전투 규칙은 `Assets/Scripts/Core/` 의 **순수 C#** (MonoBehaviour·UnityEngine 의존 금지). 테스트 가능하게.
- 화면·연출은 `Assets/Scripts/View/` 의 MonoBehaviour. Core를 호출만 한다.
- 카드 수치를 코드에 하드코딩하지 않는다. 항상 데이터(JSON)에서 읽는다.
- 테스트: `Assets/Tests/EditMode/` (Unity Test Framework). 규칙집 예시를 테스트로 만든다.
- 그래픽은 임시 도형으로 시작. 파일 이름 규칙: `Assets/Art/<분류>/<영문소문자_밑줄>.png` (예: `cards/longsword.png`). 같은 이름으로 덮어쓰면 교체되게 만든다.

## 폴더
```
Assets/Scripts/Core   전투 규칙 (순수 C#)
Assets/Scripts/View   화면, UI
Assets/Data           JSON 데이터
Assets/Art            그래픽 (기획자 담당)
Assets/Scenes         씬 (Main.unity 하나를 기본으로)
Assets/Tests/EditMode 자동 테스트
Design/               규칙집 원본·요약 (Unity가 읽지 않음)
```

## 토큰 절약
- `Library/`, `Temp/`, `Logs/`, `obj/`, `UserSettings/` 는 읽지 않는다.
- 스크린샷보다 Unity 콘솔 텍스트(MCP)를 우선 사용.
- 작업이 끝나면 무엇을 바꿨는지 2~3줄로만 보고.

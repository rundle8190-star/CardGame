---
name: new-game
description: games/ 아래에 새 게임 폴더를 TypeScript+Vite+Canvas 기본 구조로 만들 때 사용한다. 기획서(docs/design)가 있으면 그것을 따른다.
---

# 새 게임 폴더 만들기

## 순서
1. `docs/design/<게임-id>.md`가 있는지 확인한다. 없으면 `game-design` 스킬을 먼저 실행한다.
2. 루트에 `package.json`이 없으면 아래 내용으로 만든다.
   ```json
   {
     "name": "multi-games",
     "private": true,
     "workspaces": ["games/*"],
     "scripts": {
       "dev": "npm run dev -w",
       "test": "npm test --workspaces --if-present",
       "build": "npm run build --workspaces --if-present"
     }
   }
   ```
3. `games/<게임-id>/`에 다음 파일을 만든다.
   - `package.json`: name `@games/<게임-id>`, scripts `dev`=`vite`, `build`=`tsc --noEmit && vite build`, `test`=`vitest run`, devDependencies `vite`, `typescript`, `vitest`
   - `vite.config.ts`: `base: './'` (GitHub Pages 하위 경로에서 동작하도록)
   - `tsconfig.json`: `strict: true`, `target: ES2020`, `module: ESNext`, `moduleResolution: bundler`
   - `index.html`: `<canvas id="game">` 하나와 `src/main.ts` 로드, viewport 메타 포함
   - `src/main.ts`: 캔버스 크기 맞춤, `requestAnimationFrame` 루프, 고정 dt(1/60) 누적 업데이트
   - `src/game/state.ts`: 게임 상태 타입과 `createInitialState(seed)`, `update(state, dt, input)`
   - `src/render/draw.ts`: `render(ctx, state)`
   - `src/input/keyboard.ts`: 눌린 키 집합을 관리
   - `tests/state.test.ts`: 초기 상태와 update 한 번에 대한 테스트 1개 이상
   - `assets/CREDITS.md`: 빈 표
4. `npm install` 후 `npm test -w @games/<게임-id>`와 `npm run build -w @games/<게임-id>`가 통과하는지 확인한다.
5. `playtest` 스킬로 화면이 뜨는지 확인한다.

## 주의
- 상태 갱신 로직에서 `document`, `window`를 쓰지 않는다.
- 다른 게임 폴더의 코드를 직접 import하지 않는다. 공통 코드가 필요하면 `packages/shared/`를 만들자고 먼저 제안한다.

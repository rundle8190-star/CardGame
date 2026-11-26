---
name: playtest
description: 게임을 실제로 띄워서 화면, 콘솔 에러, 기본 조작을 확인하고 스크린샷을 남길 때 사용한다. 기능 구현 후나 PR 올리기 전에 실행한다.
---

# 플레이테스트

## 순서
1. 개발 서버를 백그라운드로 띄운다: `npm run dev -w @games/<게임-id> -- --port 5173 --strictPort`
2. 서버가 응답할 때까지 기다린 뒤 아래 Playwright 스크립트를 실행한다. (클라우드 환경에서는 `require('playwright')`가 바로 동작하고 브라우저 설치가 필요 없다.)
   ```js
   const { chromium } = require('playwright');
   (async () => {
     const browser = await chromium.launch();
     const errors = [];
     for (const vp of [{ width: 1280, height: 720 }, { width: 360, height: 740 }]) {
       const page = await browser.newPage({ viewport: vp, deviceScaleFactor: 2 });
       page.on('console', m => m.type() === 'error' && errors.push(m.text()));
       page.on('pageerror', e => errors.push(e.message));
       await page.goto('http://localhost:5173');
       await page.waitForTimeout(1000);
       await page.screenshot({ path: `playtest-${vp.width}-start.png` });
       // 기획서의 조작 표에 있는 입력을 몇 번 보낸다 (예: 방향키, 클릭)
       await page.keyboard.press('ArrowRight');
       await page.mouse.click(vp.width / 2, vp.height / 2);
       await page.waitForTimeout(1000);
       await page.screenshot({ path: `playtest-${vp.width}-play.png` });
       await page.close();
     }
     await browser.close();
     console.log(errors.length ? 'ERRORS:\n' + errors.join('\n') : 'NO CONSOLE ERRORS');
   })();
   ```
3. 스크린샷을 직접 열어 보고 다음을 확인한다.
   - 캔버스가 화면에 맞게 그려지는가
   - 입력 후 화면이 바뀌었는가
   - 360px 폭에서 UI가 잘리지 않는가
4. 개발 서버를 종료한다.

## 보고 형식
- 콘솔 에러 유무와 내용
- 확인한 항목별 통과/실패
- 스크린샷 경로 (PR이면 본문에 첨부)

스크린샷 파일(`playtest-*.png`)은 커밋하지 않는다.

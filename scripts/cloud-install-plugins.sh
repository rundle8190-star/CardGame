#!/usr/bin/env bash
# 클라우드 세션용: .claude/settings.json 에 적힌 플러그인 4개를 실제로 설치한다.
# 클라우드 환경의 setup script 가 Claude 시작 전에 이 파일을 실행한다.
# 실패해도 세션이 멈추지 않도록 항상 0으로 끝낸다.
command -v claude >/dev/null 2>&1 || exit 0
for m in DietrichGebert/ponytail mvanhorn/last30days-skill bradautomates/claude-video anthropics/claude-plugins-official; do
  claude plugin marketplace add "$m" >/dev/null 2>&1 || true
done
for p in ponytail@ponytail last30days@last30days-skill watch@claude-video mattpocock-skills@claude-plugins-official; do
  claude plugin install "$p" --scope user >/dev/null 2>&1 || true
done
exit 0

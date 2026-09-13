#!/usr/bin/env bash
# Starts the API and the frontend together.
# Optional: export GEMINI_API_KEY=your-key-here first to use Gemini.
set -e
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

cleanup() { kill $api $web 2>/dev/null || true; }
trap cleanup EXIT INT TERM

echo "Starting the API on :5180"
(cd "$here/backend/AnalystAI.Api" && dotnet run --urls http://localhost:5180) & api=$!

until curl -sf http://localhost:5180/api/health >/dev/null 2>&1; do sleep 1; done
echo "API is up."

echo "Starting the frontend on :5173"
(cd "$here/frontend" && npm run dev) & web=$!

echo
echo "  API      http://localhost:5180/api/health"
echo "  Frontend http://localhost:5173"
echo
wait

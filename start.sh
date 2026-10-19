#!/usr/bin/env bash
# Starts VMM Facility Management locally: builds the React frontend, starts the
# ASP.NET Core backend (which serves the frontend) on localhost, and opens the browser.
# Stop with Ctrl+C.
set -euo pipefail

cd "$(dirname "$0")"
PORT="${VMM_PORT:-5180}"
URL="http://localhost:${PORT}"

DOTNET="$(command -v dotnet || true)"
if [[ -z "$DOTNET" && -x /usr/local/share/dotnet/dotnet ]]; then
  DOTNET=/usr/local/share/dotnet/dotnet
fi
if [[ -z "$DOTNET" ]]; then
  echo "The .NET 10 SDK is required: https://dotnet.microsoft.com/download" >&2
  exit 1
fi
if ! command -v npm >/dev/null; then
  echo "Node.js (with npm) is required: https://nodejs.org" >&2
  exit 1
fi

echo "Building frontend…"
(
  cd frontend
  [[ -d node_modules ]] || npm install --no-fund --no-audit
  npm run build --silent
)

echo "Building backend…"
"$DOTNET" build backend/VmmFacility.Api -c Release --nologo -v quiet

echo "Starting VMM Facility Management at ${URL}"
export VMM_PORT="$PORT"
export ASPNETCORE_ENVIRONMENT=Production
"$DOTNET" run --project backend/VmmFacility.Api -c Release --no-build &
SERVER_PID=$!
trap 'kill "$SERVER_PID" 2>/dev/null || true' EXIT INT TERM

for _ in $(seq 1 60); do
  if curl -fs "${URL}/api/auth/session" >/dev/null 2>&1; then
    open "$URL"
    echo "Opened ${URL} in your browser. Press Ctrl+C to stop the app."
    wait "$SERVER_PID"
    exit 0
  fi
  if ! kill -0 "$SERVER_PID" 2>/dev/null; then
    echo "The backend stopped unexpectedly. See the messages above." >&2
    exit 1
  fi
  sleep 0.5
done

echo "The backend did not start within 30 seconds." >&2
exit 1

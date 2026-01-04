#!/usr/bin/env bash
set -euo pipefail

# Voice end-to-end smoke test:
# 1) Generate a short WAV/PCM audio via Azure Speech TTS REST API (compatible with SpeechToTextAsync)
# 2) Base64 it
# 3) Send it to /api/godgpt/voice/chat (SSE)
#
# Notes:
# - This script intentionally does NOT print any secrets.
# - It reads Speech config from apps/Aevatar.App/src/Aevatar.Silo/appsettings.json by default.

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

AUTH_URL="${AUTH_URL:-http://localhost:8001}"
HTTPAPI_URL="${HTTPAPI_URL:-http://localhost:8082}"
SILO_APPSETTINGS="${SILO_APPSETTINGS:-$ROOT_DIR/apps/Aevatar.App/src/Aevatar.Silo/appsettings.json}"

USERNAME="${USERNAME:-admin}"
PASSWORD="${PASSWORD:-1q2w3E*}"
CLIENT_ID="${CLIENT_ID:-AevatarAuthServer}"
SCOPE="${SCOPE:-Aevatar openid profile}"

VOICE_TEXT="${VOICE_TEXT:-Hello, this is a short voice test.}"
VOICE_LANGUAGE="${VOICE_LANGUAGE:-0}" # matches VoiceLanguageEnum integer, 0 is usually English

TTS_MAX_TIME_SECONDS="${TTS_MAX_TIME_SECONDS:-25}"
SSE_MAX_TIME_SECONDS="${SSE_MAX_TIME_SECONDS:-20}"
# Output mode:
# - summary (default): prints only key events (first text chunk, audio present, completed)
# - redact: prints SSE data but replaces huge AudioData base64 with "<base64 omitted>"
# - raw: prints SSE data as-is (may be huge)
OUTPUT_MODE="${OUTPUT_MODE:-summary}"
PRINT_AUDIO_BASE64="${PRINT_AUDIO_BASE64:-0}" # backward-compat: if 1, force raw

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || { echo "Missing dependency: $1" >&2; exit 1; }
}

require_cmd curl
require_cmd python3
require_cmd base64

get_json() {
  python3 - <<'PY'
import json,sys
path=sys.argv[1]
keys=sys.argv[2].split(".")
with open(path,"r",encoding="utf-8") as f:
    data=json.load(f)
cur=data
for k in keys:
    cur=cur.get(k)
print("" if cur is None else cur)
PY
}

echo "[1/4] Fetching auth token..."
TOKEN="$(curl -s -X POST "$AUTH_URL/connect/token" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  --data-urlencode "grant_type=password" \
  --data-urlencode "client_id=$CLIENT_ID" \
  --data-urlencode "username=$USERNAME" \
  --data-urlencode "password=$PASSWORD" \
  --data-urlencode "scope=$SCOPE" \
  | python3 -c 'import sys, json; print(json.load(sys.stdin)["access_token"])')"

echo "[2/4] Creating session..."
SESSION_ID="$(curl -s -X POST "$HTTPAPI_URL/api/godgpt/create-session" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -H "GodgptLanguage: en" \
  -d '{"guider":"","userLocalTime":"2024-12-23T10:00:00Z"}' \
  | python3 -c 'import sys, json; print(json.load(sys.stdin).get("data",""))')"

if [[ -z "$SESSION_ID" ]]; then
  echo "Failed to create session (empty session id)." >&2
  exit 1
fi

echo "[3/4] Generating voice base64 via Azure Speech TTS REST..."
SPEECH_KEY="$(python3 -c 'import json; import sys; print(json.load(open(sys.argv[1]))["Speech"]["SubscriptionKey"])' "$SILO_APPSETTINGS")"
SPEECH_REGION="$(python3 -c 'import json; import sys; print(json.load(open(sys.argv[1]))["Speech"]["Region"])' "$SILO_APPSETTINGS")"

if [[ -z "$SPEECH_KEY" || -z "$SPEECH_REGION" ]]; then
  echo "Speech config missing. Set Speech.SubscriptionKey and Speech.Region in $SILO_APPSETTINGS (or override SILO_APPSETTINGS)." >&2
  exit 1
fi
echo "[3/4] Speech config loaded (region only): region=$SPEECH_REGION"

# Issue token (valid ~10 min)
TMP_TOKEN="$(mktemp -t godgpt_speech_token_XXXXXX.txt)"

TOKEN_STATUS="$(curl -sS -o "$TMP_TOKEN" -w "%{http_code}" -X POST "https://$SPEECH_REGION.api.cognitive.microsoft.com/sts/v1.0/issueToken" \
  -H "Ocp-Apim-Subscription-Key: $SPEECH_KEY" \
  --data "")"

if [[ "$TOKEN_STATUS" != "200" ]]; then
  echo "Failed to issue Azure Speech token (http=$TOKEN_STATUS)." >&2
  exit 1
fi

SPEECH_TOKEN="$(cat "$TMP_TOKEN")"
if [[ -z "$SPEECH_TOKEN" || "${#SPEECH_TOKEN}" -lt 100 ]]; then
  echo "Azure Speech token looks invalid (too short)." >&2
  exit 1
fi
echo "[3/4] Azure Speech token issued."

TMP_MP3="$(mktemp -t godgpt_voice_XXXXXX.wav)"
TMP_SSML="$(mktemp -t godgpt_ssml_XXXXXX.xml)"
TMP_TTS_HDR="$(mktemp -t godgpt_tts_hdr_XXXXXX.txt)"
trap 'rm -f "${TMP_MP3:-}" "${TMP_SSML:-}" "${TMP_TOKEN:-}" "${TMP_TTS_HDR:-}"' EXIT

# Use a common English voice. If you use a different region/voice, override via env.
VOICE_NAME="${VOICE_NAME:-en-US-JennyNeural}"

SSML="$(python3 - <<PY
import html
voice_name = html.escape("$VOICE_NAME")
text = html.escape("$VOICE_TEXT")
print("<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-US'><voice name='" + voice_name + "'>" + text + "</voice></speak>")
PY
)"
printf "%s" "$SSML" > "$TMP_SSML"

curl --http1.1 -s -X POST "https://$SPEECH_REGION.tts.speech.microsoft.com/cognitiveservices/v1" \
  -H "Authorization: Bearer $SPEECH_TOKEN" \
  -H "Content-Type: application/ssml+xml" \
  -H "X-Microsoft-OutputFormat: riff-16khz-16bit-mono-pcm" \
  --max-time "$TTS_MAX_TIME_SECONDS" \
  --data-binary "@$TMP_SSML" \
  -D "$TMP_TTS_HDR" \
  -o "$TMP_MP3" || { echo "TTS request failed (curl exit $?)." >&2; exit 1; }

if [[ ! -s "$TMP_MP3" ]]; then
  echo "TTS returned an empty audio file." >&2
  exit 1
fi

# Basic sanity check: reject HTML error bodies.
if head -c 5 "$TMP_MP3" | grep -qi "<html"; then
  echo "TTS returned HTML error body (not audio). First headers:" >&2
  head -n 20 "$TMP_TTS_HDR" >&2
  exit 1
fi

echo "[3/4] TTS audio generated: $(wc -c <"$TMP_MP3") bytes"

VOICE_BASE64="$(base64 <"$TMP_MP3" | tr -d '\n')"
if [[ -z "$VOICE_BASE64" ]]; then
  echo "Base64 encoding produced empty output." >&2
  exit 1
fi

echo "[4/4] Calling voice chat SSE (Ctrl+C to stop if it doesn't auto-complete)..."
CHAT_ID="$(python3 -c 'import uuid; print(str(uuid.uuid4()))')"

SSE_CMD=(curl -s -N -X POST "$HTTPAPI_URL/api/godgpt/voice/chat" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -H "Accept: text/event-stream" \
  --max-time "$SSE_MAX_TIME_SECONDS" \
  -d "$(python3 - <<PY
import json
req={
  "sessionId":"$SESSION_ID",
  "content":"$VOICE_BASE64",
  "messageType": 1,
  "voiceLanguage": int("$VOICE_LANGUAGE"),
  "voiceDurationSeconds": 1.0,
  "region": ""
}
print(json.dumps(req))
PY
)")

# Backward compat: PRINT_AUDIO_BASE64=1 forces raw.
if [[ "$PRINT_AUDIO_BASE64" == "1" ]]; then
  OUTPUT_MODE="raw"
fi

run_sse_raw() {
  "${SSE_CMD[@]}"
}

run_sse_redact() {
  # Important: users may pipe this script to `head` which closes stdout early.
  # That leads to curl exit code 23 (failed writing body). Treat it as a benign early-exit.
  set +o pipefail
  "${SSE_CMD[@]}" | python3 -u - <<'PY'
import sys,re
for line in sys.stdin:
    if not line.startswith("data: "):
        sys.stdout.write(line); sys.stdout.flush()
        continue
    payload = line[len("data: "):].strip()
    if not payload or payload == "[DONE]":
        sys.stdout.write(line); sys.stdout.flush()
        continue
    if '"AudioData":"' in payload:
        payload = re.sub(r'"AudioData":"[^"]*"', '"AudioData":"<base64 omitted>"', payload)
    sys.stdout.write("data: " + payload + "\n\n")
    sys.stdout.flush()
PY
  local curl_ec=${PIPESTATUS[0]:-0}
  local py_ec=${PIPESTATUS[1]:-0}
  set -o pipefail

  # 23 means "failed writing body" (typically because stdout was closed by a pipe like `head`).
  if [[ "$curl_ec" -eq 23 ]]; then
    echo "[info] curl exited 23 (stdout closed early). Treating as benign." >&2
    return 0
  fi
  [[ "$curl_ec" -ne 0 ]] && return "$curl_ec"
  [[ "$py_ec" -ne 0 ]] && return "$py_ec"
  return 0
}

run_sse_summary() {
  # Do NOT pipe `curl | python` here.
  # Some IDE terminals/log collectors may close stdout early, which kills python (SIGPIPE)
  # and then makes curl exit 23. Instead, save the SSE stream to a temp file and parse it after.
  local tmp_sse
  tmp_sse="$(mktemp -t godgpt_voice_sse_XXXXXX.txt)"
  trap 'rm -f "${tmp_sse:-}"' RETURN

  echo "[summary] Capturing SSE to temp file..."
  # Keep stderr clean; we only need the body.
  "${SSE_CMD[@]}" -o "$tmp_sse" || true

  python3 - <<PY
import re,sys
path = r"$tmp_sse"
try:
    data = open(path, "r", encoding="utf-8", errors="ignore").read().splitlines()
except Exception as e:
    print(f"[summary] failed_to_read_sse_file: {e}")
    sys.exit(0)

re_chatid=re.compile(r'"ChatId":"([^"]+)"')
re_session=re.compile(r'"SessionId":"([^"]+)"')
re_serial=re.compile(r'"SerialNumber":([0-9]+)')
re_audio=re.compile(r'"AudioData":"([^"]*)"')

completed = any(line.startswith("event: completed") for line in data)

first_text = None
first_audio = None
last_chunk = None

for line in data:
    if not line.startswith("data: "):
        continue
    payload=line[len("data: "):].strip()
    if not payload:
        continue
    if first_text is None and '"Response":"' in payload and '"AudioData":null' in payload:
        first_text = payload
    if first_audio is None and '"AudioData":"' in payload:
        m = re_audio.search(payload)
        b64_len = len(m.group(1)) if m else 0
        first_audio = (payload, b64_len)
    if '"IsLastChunk":true' in payload:
        last_chunk = payload

def extract(pat, s, default=""):
    m=pat.search(s or "")
    return m.group(1) if m else default

def fmt(kind, payload):
    chat_id = extract(re_chatid, payload)
    session_id = extract(re_session, payload)
    serial = extract(re_serial, payload, "0")
    return f"[summary] {kind}: serial={serial} chat_id={chat_id} session_id={session_id}"

print(f"[summary] completed={str(completed).lower()}")
if first_text:
    print(fmt("first_text", first_text))
if first_audio:
    payload, b64_len = first_audio
    print(fmt("first_audio", payload) + f" base64_len={b64_len}")
if last_chunk:
    print(fmt("last_chunk", last_chunk))
PY

  # In summary mode, treat curl write errors as non-fatal because we still can parse what we captured.
  return 0
}

case "$OUTPUT_MODE" in
  raw) run_sse_raw ;;
  redact) run_sse_redact ;;
  summary) run_sse_summary ;;
  *)
    echo "Unknown OUTPUT_MODE: $OUTPUT_MODE (expected: summary|redact|raw)" >&2
    exit 1
    ;;
esac



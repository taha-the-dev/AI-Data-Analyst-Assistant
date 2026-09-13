#!/usr/bin/env bash
# Proves the frontend and the API are actually wired together.
#
# Every call in frontend/src/lib/api.js is made here against the frontend's own
# origin, so the Vite proxy hop is part of what is tested — a broken proxy, a
# renamed route or a changed status code shows up as a BAD line rather than as a
# blank screen. The audit signs up a throwaway account, works through it, and
# deletes it at the end. Start both servers, then:
#
#   bash link-audit.sh                      # http://localhost:5173
#   bash link-audit.sh http://localhost:4173
#
BASE="${1:-http://localhost:5173}"
PASS=0; FAIL=0

TMP=$(mktemp -d)
JAR="$TMP/session.jar"
EMAIL="link-audit-$(date +%s)-$RANDOM@example.test"
PASSWORD="link audit passphrase $RANDOM $RANDOM"
CREDENTIALS="{\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\"}"

ok ()  { printf '  OK   %-3s  %-34s %s %s\n' "$1" "$2" "$3" "$4"; PASS=$((PASS+1)); }
bad () { printf '  BAD  %-3s  %-34s %s %s  (expected %s)\n' "$1" "$2" "$3" "$4" "$5"; FAIL=$((FAIL+1)); }

# What a browser on the frontend's origin sends: the session cookie, the header
# every request that changes data must carry, and the Origin and Fetch Metadata
# labels the API checks. Without the last two, a request the API refused in the
# browser still passed here.
call () { curl -s -b "$JAR" -c "$JAR" -H "X-DataMind-Csrf: 1" -H "Origin: $BASE" -H "Sec-Fetch-Site: same-origin" "$@"; }

hit () { # hit <expected> <caller> <method> <path> [curl args...]
  local expect="$1" caller="$2" method="$3" path="$4"; shift 4
  local code
  code=$(call -o "$TMP/last.json" -w "%{http_code}" -X "$method" "$@" "$BASE$path")
  if [ "$code" = "$expect" ]; then ok "$code" "$caller" "$method" "$path"; else bad "$code" "$caller" "$method" "$path" "$expect"; fi
}

id_of () { grep -o '"id":[0-9]*' "$1" | head -1 | cut -d: -f2; }

# If the audit stops part-way, its account still goes.
trap 'call -o /dev/null -X DELETE -H "Content-Type: application/json" -d "{\"password\":\"$PASSWORD\"}" "$BASE/api/auth/account"; rm -rf "$TMP"' EXIT

echo "Frontend -> API linking, via $BASE"
echo

hit 200 "api.health()"              GET  "/api/health"
hit 201 "api.auth.signUp()"         POST "/api/auth/signup" -H "Content-Type: application/json" -d "$CREDENTIALS"
if [ "$FAIL" -ne 0 ]; then
  echo "  could not create the audit's account; is the API running?"
  exit 1
fi
hit 200 "api.auth.me()"             GET  "/api/auth/me"
hit 200 "api.status()"              GET  "/api/status"

# Nothing is seeded, so the audit uploads the file it then reads through.
printf 'Order Date,Client,Item,Segment,Units,Unit Price,Total,Country,State\n2026-02-01,Contoso,Widget,Hardware,2,50.00,100.00,France,Completed\n2026-03-04,Fabrikam,Gadget,Cloud,1,80.00,80.00,Germany,Completed\n' > "$TMP/seed.csv"
hit 201 "api.datasets.upload(file)" POST "/api/datasets/upload" -F "file=@$TMP/seed.csv"
DS=$(id_of "$TMP/last.json")

hit 200 "api.datasets.list()"       GET  "/api/datasets?page=1&pageSize=6&sort=updated&dir=desc"
hit 200 "api.datasets.get(id)"      GET  "/api/datasets/$DS"
hit 200 "api.explorer.columns()"    GET  "/api/explorer/columns"
hit 200 "api.explorer.rows()"       GET  "/api/explorer/rows?datasetId=$DS&page=1&pageSize=25&sort=revenue&dir=desc"
hit 200 "api.dashboard(id)"         GET  "/api/dashboard?datasetId=$DS"
hit 200 "api.analytics(id)"         GET  "/api/analytics?datasetId=$DS"
hit 200 "api.chat.sessions()"       GET  "/api/chat/sessions"
hit 200 "api.reports.list()"        GET  "/api/reports"
hit 200 "api.settings.get()"        GET  "/api/settings"
hit 200 "api.settings.providers()"  GET  "/api/providers"
hit 200 "api.runSpec(spec, id)"     POST "/api/query/run" -H "Content-Type: application/json" -d '{"spec":{"intent":"aggregate","groupBy":"region","metric":"revenue","aggregate":"sum","limit":5},"datasetId":'"$DS"'}'

hit 201 "api.chat.create()"         POST "/api/chat/sessions" -H "Content-Type: application/json" -d '{"title":"link audit"}'
SID=$(id_of "$TMP/last.json")
hit 200 "api.chat.messages(id)"     GET  "/api/chat/sessions/$SID"
hit 200 "api.chat.save(id, title)"  PUT  "/api/chat/sessions/$SID" -H "Content-Type: application/json" -d '{"title":"link audit saved"}'
hit 200 "api.chat.ask(id, q)"       POST "/api/chat/sessions/$SID/ask?datasetId=$DS" -H "Content-Type: application/json" -d '{"question":"Revenue by region"}'

# The assistant streams over SSE, so it is checked for its content type.
CT=$(call -o /dev/null -N --max-time 20 -w "%{content_type}" "$BASE/api/chat/sessions/$SID/stream?question=revenue%20by%20region&datasetId=$DS")
case "$CT" in
  text/event-stream*) ok "sse" "api.chat.streamUrl()" "GET" "/api/chat/sessions/{id}/stream";;
  *) bad "$CT" "api.chat.streamUrl()" "GET" "/api/chat/sessions/{id}/stream" "text/event-stream";;
esac

hit 204 "api.chat.remove(id)"       DELETE "/api/chat/sessions/$SID"

hit 201 "api.reports.create(title)" POST "/api/reports" -H "Content-Type: application/json" -d '{"title":"link audit report","datasetId":'"$DS"'}'
RID=$(id_of "$TMP/last.json")
hit 200 "api.reports.get(id)"       GET  "/api/reports/$RID"
hit 204 "api.reports.remove(id)"    DELETE "/api/reports/$RID"

hit 200 "Settings screen: PUT"      PUT  "/api/settings" -H "Content-Type: application/json" -d '{"providerId":"keyword","modelName":"rules-v1"}'
hit 204 "api.datasets.remove(id)"   DELETE "/api/datasets/$DS"

hit 204 "api.auth.signOut()"        POST "/api/auth/logout"
hit 401 "session ended"             GET  "/api/datasets"
hit 200 "api.auth.signIn()"         POST "/api/auth/login" -H "Content-Type: application/json" -d "$CREDENTIALS"

echo
echo "Swagger"
hit 200 "Swagger UI"                GET  "/swagger/index.html"
hit 200 "OpenAPI document"          GET  "/openapi/v1.json"

echo
hit 204 "api.auth.deleteAccount()"  DELETE "/api/auth/account" -H "Content-Type: application/json" -d "{\"password\":\"$PASSWORD\"}"

echo
echo "linked $PASS, broken $FAIL"
[ "$FAIL" -eq 0 ]

#!/usr/bin/env bash
# Exercises every endpoint and asserts the status code.
# Usage: bash smoke-test.sh [base-url]
#
# Every data endpoint needs a signed-in account, so the run signs up two
# throwaway accounts — one that does the work, and one that proves it cannot
# see any of it — and deletes both at the end, with everything they created.
BASE="${1:-http://localhost:5180}"
PASS=0
FAIL=0

TMP=$(mktemp -d)
OWNER_JAR="$TMP/owner.jar"
OTHER_JAR="$TMP/other.jar"
STAMP="$(date +%s)-$RANDOM"
OWNER_EMAIL="smoke-owner-$STAMP@example.test"
OTHER_EMAIL="smoke-other-$STAMP@example.test"
PASSWORD="smoke passphrase $RANDOM $RANDOM"
JSON=(-H "Content-Type: application/json")
CSRF=(-H "X-DataMind-Csrf: 1")

report () { # report <code> <expected> <name>
  if [ "$1" = "$2" ]; then
    printf '  PASS  %-3s  %s\n' "$1" "$3"; PASS=$((PASS+1))
  else
    printf '  FAIL  %-3s  %s  (expected %s)\n' "$1" "$3" "$2"; FAIL=$((FAIL+1))
  fi
}

assert () { # assert <true|false> <name>
  if [ "$1" = "true" ]; then
    printf '  PASS  ---  %s\n' "$2"; PASS=$((PASS+1))
  else
    printf '  FAIL  ---  %s\n' "$2"; FAIL=$((FAIL+1))
  fi
}

# The body of every call is kept, so a case that creates something can read its
# id back instead of repeating the request to get one.
check () { # check <expected> <name> <curl-args...> — as the account doing the work
  local expect="$1" name="$2"; shift 2
  report "$(curl -s -o "$TMP/last.json" -w "%{http_code}" -b "$OWNER_JAR" -c "$OWNER_JAR" "${CSRF[@]}" "$@")" "$expect" "$name"
}

check_other () { # check_other <expected> <name> <curl-args...> — as the second account
  local expect="$1" name="$2"; shift 2
  report "$(curl -s -o "$TMP/last.json" -w "%{http_code}" -b "$OTHER_JAR" -c "$OTHER_JAR" "${CSRF[@]}" "$@")" "$expect" "$name"
}

raw () { # raw <expected> <name> <curl-args...> — no session, no header unless given
  local expect="$1" name="$2"; shift 2
  report "$(curl -s -o "$TMP/last.json" -w "%{http_code}" "$@")" "$expect" "$name"
}

owner () { curl -s -b "$OWNER_JAR" -c "$OWNER_JAR" "${CSRF[@]}" "$@"; }
id_of () { grep -o '"id":[0-9]*' "$1" | head -1 | cut -d: -f2; }
credentials () { printf '{"email":"%s","password":"%s"}' "$1" "$2"; }

# If the run stops part-way, the accounts it made still go. After the counted
# deletions at the end these are no-ops.
cleanup () {
  for jar in "$OWNER_JAR" "$OTHER_JAR"; do
    curl -s -o /dev/null -b "$jar" "${CSRF[@]}" "${JSON[@]}" -X DELETE \
      -d "{\"password\":\"$PASSWORD\"}" "$BASE/api/auth/account"
  done
  rm -rf "$TMP"
}
trap cleanup EXIT

cat > "$TMP/sample.csv" <<'CSV'
id,name,dept,salary,joined,active
1,Ada,Engineering,64000,2019-04-08,true
2,Sam,Sales,91500,2021-11-01,true
3,Ren,Engineering,,2018-02-15,false
CSV

echo "AnalystAI API smoke test -> $BASE"
echo

echo "Health"
raw 200 "GET  /api/health (no session)"             "$BASE/api/health"

echo "Accounts"
raw 401 "GET  /api/datasets (no session)"           "$BASE/api/datasets"
raw 401 "GET  /api/status (no session)"             "$BASE/api/status"
raw 401 "GET  /api/auth/me (no session)"            "$BASE/api/auth/me"
raw 403 "POST /api/auth/signup (no CSRF header)"    -X POST "${JSON[@]}" -d "$(credentials "$OWNER_EMAIL" "$PASSWORD")" "$BASE/api/auth/signup"
check 400 "POST /api/auth/signup (short password)"  -X POST "${JSON[@]}" -d "$(credentials "$OWNER_EMAIL" "short")" "$BASE/api/auth/signup"
check 400 "POST /api/auth/signup (not an address)"  -X POST "${JSON[@]}" -d "$(credentials "not-an-address" "$PASSWORD")" "$BASE/api/auth/signup"
check 201 "POST /api/auth/signup"                   -X POST "${JSON[@]}" -d "$(credentials "$OWNER_EMAIL" "$PASSWORD")" "$BASE/api/auth/signup"
check 409 "POST /api/auth/signup (address taken)"   -X POST "${JSON[@]}" -d "$(credentials "$OWNER_EMAIL" "$PASSWORD")" "$BASE/api/auth/signup"
check 200 "GET  /api/auth/me"                       "$BASE/api/auth/me"
check 204 "POST /api/auth/logout"                   -X POST "$BASE/api/auth/logout"
check 401 "GET  /api/auth/me (signed out)"          "$BASE/api/auth/me"
check 401 "POST /api/auth/login (wrong password)"   -X POST "${JSON[@]}" -d "$(credentials "$OWNER_EMAIL" "wrong passphrase entirely")" "$BASE/api/auth/login"
check 401 "POST /api/auth/login (no such account)"  -X POST "${JSON[@]}" -d "$(credentials "nobody-$STAMP@example.test" "$PASSWORD")" "$BASE/api/auth/login"
check 200 "POST /api/auth/login"                    -X POST "${JSON[@]}" -d "$(credentials "$OWNER_EMAIL" "$PASSWORD")" "$BASE/api/auth/login"
check 200 "GET  /api/status"                        "$BASE/api/status"

echo "Cross-site requests"
raw 403 "POST without the CSRF header"              -b "$OWNER_JAR" -X POST "${JSON[@]}" -d '{"title":"forged"}' "$BASE/api/chat/sessions"
raw 403 "POST labelled cross-site"                  -b "$OWNER_JAR" "${CSRF[@]}" -H "Sec-Fetch-Site: cross-site" -X POST "${JSON[@]}" -d '{"title":"forged"}' "$BASE/api/chat/sessions"
raw 403 "POST from a foreign Origin"                -b "$OWNER_JAR" "${CSRF[@]}" -H "Origin: https://attacker.example" -X POST "${JSON[@]}" -d '{"title":"forged"}' "$BASE/api/chat/sessions"
raw 403 "GET  labelled cross-site"                  -b "$OWNER_JAR" -H "Sec-Fetch-Site: cross-site" "$BASE/api/datasets"

# The suite brings its own data and the account deletion at the end removes it.
owner -o "$TMP/seed.json" -F "file=@$TMP/sample.csv" "$BASE/api/datasets/upload"
DATASET_ID=$(id_of "$TMP/seed.json")
if [ -z "$DATASET_ID" ]; then
  echo "  could not upload the fixture the suite needs; is the API running?"
  exit 1
fi
echo
echo "Fixture dataset -> id $DATASET_ID"

echo "Datasets"
check 200 "GET  /api/datasets"                      "$BASE/api/datasets"
check 200 "GET  /api/datasets (search+sort+page)"   "$BASE/api/datasets?search=a&sort=rows&dir=desc&page=1&pageSize=5"
check 200 "GET  /api/datasets/<fixture>"            "$BASE/api/datasets/$DATASET_ID"
check 404 "GET  /api/datasets/9999"                 "$BASE/api/datasets/9999"
check 201 "POST /api/datasets/upload (csv)"         -F "file=@$TMP/sample.csv" "$BASE/api/datasets/upload"
FIRST_ID=$(id_of "$TMP/last.json")
check 400 "POST /api/datasets/upload (no file)"     -X POST -F "notfile=x" "$BASE/api/datasets/upload"
check 404 "DEL  /api/datasets/9999"                 -X DELETE "$BASE/api/datasets/9999"

# An upload must be queryable the moment it lands: rows are mapped onto the
# queryable schema, not just profiled.
check 201 "POST /api/datasets/upload (again)"       -F "file=@$TMP/sample.csv" "$BASE/api/datasets/upload"
NEW_ID=$(id_of "$TMP/last.json")
check 200 "GET  /api/datasets/<uploaded>"           "$BASE/api/datasets/$NEW_ID"
check 200 "GET  /api/explorer/rows (uploaded)"      "$BASE/api/explorer/rows?datasetId=$NEW_ID"
check 200 "GET  /api/dashboard (uploaded)"          "$BASE/api/dashboard?datasetId=$NEW_ID"
check 200 "GET  /api/analytics (uploaded)"          "$BASE/api/analytics?datasetId=$NEW_ID"
check 204 "DEL  /api/datasets/<uploaded>"           -X DELETE "$BASE/api/datasets/$NEW_ID"
check 204 "DEL  /api/datasets/<first upload>"       -X DELETE "$BASE/api/datasets/$FIRST_ID"

echo "Explorer"
check 200 "GET  /api/explorer/columns"              "$BASE/api/explorer/columns"
check 200 "GET  /api/explorer/rows"                 "$BASE/api/explorer/rows?datasetId=$DATASET_ID&page=1&pageSize=10"
check 200 "GET  /api/explorer/rows (filtered)"      "$BASE/api/explorer/rows?datasetId=$DATASET_ID&filter=revenue:gt:100&filter=region:eq:Europe"
check 400 "GET  /api/explorer/rows (bad filter)"    "$BASE/api/explorer/rows?datasetId=$DATASET_ID&filter=revenue-gt-10000"
check 400 "GET  /api/explorer/rows (bad column)"    "$BASE/api/explorer/rows?datasetId=$DATASET_ID&filter=nope:eq:1"
check 400 "GET  /api/explorer/rows (bad sort)"      "$BASE/api/explorer/rows?datasetId=$DATASET_ID&sort=bogus"

echo "Insights"
check 200 "GET  /api/dashboard"                     "$BASE/api/dashboard"
check 404 "GET  /api/dashboard?datasetId=9999"      "$BASE/api/dashboard?datasetId=9999"
check 200 "GET  /api/analytics"                     "$BASE/api/analytics"
check 404 "GET  /api/analytics?datasetId=9999"      "$BASE/api/analytics?datasetId=9999"

echo "Chat"
check 200 "GET  /api/chat/sessions"                 "$BASE/api/chat/sessions"
check 201 "POST /api/chat/sessions"                 -X POST "${JSON[@]}" -d '{"title":"smoke session"}' "$BASE/api/chat/sessions"
SESSION_ID=$(id_of "$TMP/last.json")
check 200 "GET  /api/chat/sessions/<created>"       "$BASE/api/chat/sessions/$SESSION_ID"
check 200 "PUT  /api/chat/sessions (save)"          -X PUT "${JSON[@]}" -d '{"title":"Saved by the smoke test"}' "$BASE/api/chat/sessions/$SESSION_ID"
check 400 "PUT  /api/chat/sessions (empty title)"   -X PUT "${JSON[@]}" -d '{"title":"  "}' "$BASE/api/chat/sessions/$SESSION_ID"
check 400 "PUT  /api/chat/sessions (bad json)"      -X PUT "${JSON[@]}" --data-binary '{"title": broken}' "$BASE/api/chat/sessions/$SESSION_ID"
check 404 "PUT  /api/chat/sessions/9999"            -X PUT "${JSON[@]}" -d '{"title":"nope"}' "$BASE/api/chat/sessions/9999"
check 404 "GET  /api/chat/sessions/9999"            "$BASE/api/chat/sessions/9999"
check 200 "POST /api/chat/sessions/<id>/ask"        -X POST "${JSON[@]}" -d '{"question":"Which category earns the most revenue?"}' "$BASE/api/chat/sessions/$SESSION_ID/ask"
check 400 "POST /api/chat/sessions/<id>/ask (empty)" -X POST "${JSON[@]}" -d '{"question":"  "}' "$BASE/api/chat/sessions/$SESSION_ID/ask"
LONG_QUESTION=$(printf 'why %.0s' $(seq 1 300))
check 400 "POST /api/chat/sessions/<id>/ask (too long)" -X POST "${JSON[@]}" -d "{\"question\":\"$LONG_QUESTION\"}" "$BASE/api/chat/sessions/$SESSION_ID/ask"
check 404 "POST /api/chat/sessions/9999/ask"        -X POST "${JSON[@]}" -d '{"question":"hi"}' "$BASE/api/chat/sessions/9999/ask"
check 200 "GET  /api/chat/sessions/<id>/stream"     --max-time 25 "$BASE/api/chat/sessions/$SESSION_ID/stream?question=revenue%20by%20region"
check 200 "POST /api/query/run"                     -X POST "${JSON[@]}" -d '{"spec":{"intent":"aggregate","groupBy":"region","metric":"revenue","aggregate":"sum","limit":5}}' "$BASE/api/query/run"
check 400 "POST /api/query/run (bad groupBy)"       -X POST "${JSON[@]}" -d '{"spec":{"groupBy":"nope"}}' "$BASE/api/query/run"
check 400 "POST /api/query/run (bad metric)"        -X POST "${JSON[@]}" -d '{"spec":{"groupBy":"region","metric":"nope"}}' "$BASE/api/query/run"
check 400 "POST /api/query/run (bad aggregate)"     -X POST "${JSON[@]}" -d '{"spec":{"groupBy":"region","aggregate":"median"}}' "$BASE/api/query/run"
check 404 "POST /api/query/run (missing dataset)"   -X POST "${JSON[@]}" -d '{"spec":{"groupBy":"region"},"datasetId":9999}' "$BASE/api/query/run"

echo "Reports"
check 200 "GET  /api/reports"                       "$BASE/api/reports"
check 404 "GET  /api/reports/9999"                  "$BASE/api/reports/9999"
check 404 "DEL  /api/reports/9999"                  -X DELETE "$BASE/api/reports/9999"
check 404 "POST /api/reports (missing dataset)"     -X POST "${JSON[@]}" -d '{"title":"nope","datasetId":9999}' "$BASE/api/reports"
check 201 "POST /api/reports"                       -X POST "${JSON[@]}" -d "{\"title\":\"smoke report\",\"datasetId\":$DATASET_ID}" "$BASE/api/reports"
REPORT_ID=$(id_of "$TMP/last.json")
check 200 "GET  /api/reports/<created>"             "$BASE/api/reports/$REPORT_ID"

# A report's body is composed from its source file's rows on every read, so a
# report whose file is deleted says the source is gone rather than printing
# another file's figures under its name. The file gets a name nothing else in
# the run uses, because a report binds to its source by name.
cp "$TMP/sample.csv" "$TMP/smoke-orphan-source.csv"
owner -o "$TMP/orphan.json" -F "file=@$TMP/smoke-orphan-source.csv" "$BASE/api/datasets/upload"
ORPHAN_DATASET=$(id_of "$TMP/orphan.json")
owner -o "$TMP/orphan-report.json" -X POST "${JSON[@]}" -d "{\"title\":\"smoke orphan\",\"datasetId\":$ORPHAN_DATASET}" "$BASE/api/reports"
ORPHAN_REPORT=$(id_of "$TMP/orphan-report.json")
owner -o /dev/null -X DELETE "$BASE/api/datasets/$ORPHAN_DATASET"
check 410 "GET  /api/reports/<source deleted>"      "$BASE/api/reports/$ORPHAN_REPORT"
check 404 "GET  /api/reports/<source deleted>?datasetId=9999" "$BASE/api/reports/$ORPHAN_REPORT?datasetId=9999"
check 200 "GET  /api/reports/<source deleted>?datasetId=<other>" "$BASE/api/reports/$ORPHAN_REPORT?datasetId=$DATASET_ID"
check 204 "DEL  /api/reports/<source deleted>"      -X DELETE "$BASE/api/reports/$ORPHAN_REPORT"

echo "Settings"
check 200 "GET  /api/providers"                     "$BASE/api/providers"
check 200 "GET  /api/settings"                      "$BASE/api/settings"
check 200 "PUT  /api/settings"                      -X PUT "${JSON[@]}" -d '{"providerId":"keyword","modelName":"rules-v1"}' "$BASE/api/settings"
check 400 "PUT  /api/settings (bad model)"          -X PUT "${JSON[@]}" -d '{"providerId":"keyword","modelName":"claude-opus-5"}' "$BASE/api/settings"
check 400 "PUT  /api/settings (bad provider)"       -X PUT "${JSON[@]}" -d '{"providerId":"openai","modelName":"gpt"}' "$BASE/api/settings"
# Ollama was listed as a provider while "not implemented yet"; it is no longer offered.
check 400 "PUT  /api/settings (retired provider)"   -X PUT "${JSON[@]}" -d '{"providerId":"ollama","modelName":"llama3.1:8b"}' "$BASE/api/settings"

echo "Assistant provider"
check 200 "PUT  /api/settings (select gemini)"      -X PUT "${JSON[@]}" -d '{"providerId":"gemini","modelName":"gemini-2.5-flash"}' "$BASE/api/settings"
check 400 "PUT  /api/settings (gemini + wrong model)" -X PUT "${JSON[@]}" -d '{"providerId":"gemini","modelName":"llama3.1:8b"}' "$BASE/api/settings"
# Answers must keep working whether or not a Gemini key is configured.
check 200 "POST /ask while gemini selected"         -X POST "${JSON[@]}" -d '{"question":"Revenue by region"}' "$BASE/api/chat/sessions/$SESSION_ID/ask"
check 200 "PUT  /api/settings (back to built-in)"   -X PUT "${JSON[@]}" -d '{"providerId":"keyword","modelName":"rules-v1"}' "$BASE/api/settings"
# With no key set the resolver falls back to the built-in planner, so every
# case here holds either way.
check 400 "PUT  /api/settings (openrouter + wrong model)" -X PUT "${JSON[@]}" -d '{"providerId":"openrouter","modelName":"gpt-9"}' "$BASE/api/settings"
check 200 "PUT  /api/settings (select openrouter)"  -X PUT "${JSON[@]}" -d '{"providerId":"openrouter","modelName":"nvidia/nemotron-3-super-120b-a12b:free"}' "$BASE/api/settings"
check 200 "POST /ask while openrouter selected"     --max-time 120 -X POST "${JSON[@]}" -d '{"question":"Revenue by category"}' "$BASE/api/chat/sessions/$SESSION_ID/ask"

echo "Isolation between accounts"
check_other 201 "POST /api/auth/signup (second account)" -X POST "${JSON[@]}" -d "$(credentials "$OTHER_EMAIL" "$PASSWORD")" "$BASE/api/auth/signup"
check_other 200 "GET  /api/datasets (second account)"    "$BASE/api/datasets"
assert "$(grep -q '"total":0' "$TMP/last.json" && echo true || echo false)" "second account's library is empty"
check_other 404 "GET  another account's dataset"         "$BASE/api/datasets/$DATASET_ID"
check_other 404 "GET  another account's rows"            "$BASE/api/explorer/rows?datasetId=$DATASET_ID"
check_other 404 "GET  another account's dashboard"       "$BASE/api/dashboard?datasetId=$DATASET_ID"
check_other 404 "DEL  another account's dataset"         -X DELETE "$BASE/api/datasets/$DATASET_ID"
check 200 "GET  /api/datasets/<fixture> (still there)"   "$BASE/api/datasets/$DATASET_ID"
check_other 404 "GET  another account's conversation"    "$BASE/api/chat/sessions/$SESSION_ID"
check_other 404 "POST ask in another account's conversation" -X POST "${JSON[@]}" -d '{"question":"Revenue by region"}' "$BASE/api/chat/sessions/$SESSION_ID/ask"
check_other 404 "GET  another account's report"          "$BASE/api/reports/$REPORT_ID"
check_other 404 "DEL  another account's report"          -X DELETE "$BASE/api/reports/$REPORT_ID"
check_other 200 "GET  /api/settings (second account)"    "$BASE/api/settings"
assert "$(grep -q '"providerId":"keyword"' "$TMP/last.json" && echo true || echo false)" "second account keeps its own planner choice"

echo "Cleanup"
check 204 "DEL  /api/reports/<created>"             -X DELETE "$BASE/api/reports/$REPORT_ID"
check 204 "DEL  /api/chat/sessions/<created>"       -X DELETE "$BASE/api/chat/sessions/$SESSION_ID"
check 204 "DEL  /api/datasets/<fixture>"            -X DELETE "$BASE/api/datasets/$DATASET_ID"

echo "Account deletion"
check 403 "DEL  /api/auth/account (wrong password)" -X DELETE "${JSON[@]}" -d '{"password":"not the password at all"}' "$BASE/api/auth/account"
check 204 "DEL  /api/auth/account"                  -X DELETE "${JSON[@]}" -d "{\"password\":\"$PASSWORD\"}" "$BASE/api/auth/account"
check 401 "GET  /api/datasets (account deleted)"    "$BASE/api/datasets"
check 401 "POST /api/auth/login (account deleted)"  -X POST "${JSON[@]}" -d "$(credentials "$OWNER_EMAIL" "$PASSWORD")" "$BASE/api/auth/login"
check_other 204 "DEL  /api/auth/account (second account)" -X DELETE "${JSON[@]}" -d "{\"password\":\"$PASSWORD\"}" "$BASE/api/auth/account"

echo
echo "passed $PASS, failed $FAIL"
[ "$FAIL" -eq 0 ]

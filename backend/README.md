# AI Analyst — API

ASP.NET Core **.NET 10** Web API for the AI Analyst frontend.

> Note on the framework: there is no ".NET Framework 10" — .NET Framework ended
> at 4.8.1. This targets `net10.0`, the current .NET, which is what the tooling
> on this machine provides (SDK 10.0.303).

## Run it

```bash
dotnet run --project AnalystAI.Api --urls http://localhost:5180
```

The database is SQLite. Migrations create it on first start, and it holds
nothing until an account signs up — no default account, no sample rows. Delete
`AnalystAI.Api/analystai.db` to start over.

A database created before accounts existed has no migration history and rows
that belong to no one. The API moves it aside as
`analystai.pre-accounts-<time>.db` — never deletes it — and creates a new one.

Given a PostgreSQL connection string instead — key=value, or a `postgresql://`
URL such as the one Neon provides — the API runs on PostgreSQL, which is how
it is hosted: on Render, with a Neon database (see "Frontend on Vercel, API on
Render" in the repository's README, and `render.yaml`). The model is the
same on both; migrations are not, so each provider has its own context type:
`SqliteAppDbContext` with `Data/Migrations`, and `PostgresAppDbContext` with
`Data/Migrations/Postgres`. The keys that encrypt sessions are stored in the
database on either, so a restart signs nobody out.

To change the schema, edit the entities and add a migration for both:

```bash
dotnet ef migrations add <Name> --project AnalystAI.Api --context SqliteAppDbContext --output-dir Data/Migrations
```

```bash
dotnet ef migrations add <Name> --project AnalystAI.Api --context PostgresAppDbContext --output-dir Data/Migrations/Postgres --namespace AnalystAI.Api.Data.Migrations.Postgres
```

## Accounts

Email and password, through ASP.NET Core Identity. The session is an `HttpOnly`,
`SameSite=Strict` cookie (`Secure` and `__Host-` prefixed outside development).

- **Isolation** lives in `Data/AppDbContext.cs`, not in each endpoint. Every
  owned entity implements `IOwned`; the context filters every query by the
  signed-in account and stamps it on every insert, and an update can never
  change a record's owner. An id from another account is simply not found.
- **Passwords**: at least 12 characters, PBKDF2-hashed. 5 failures in a row lock
  an account for 15 minutes. Sign-in answers identically, and in the same time,
  for a wrong password, a locked account and an address with no account.
- **Rate limits**: sign-in, sign-up and account deletion at 20 a minute per
  address (`Security:AuthRequestsPerMinute`); assistant questions at 30 a minute
  per account; everything else at 600.
- **Cross-site requests** (`Security/RequestGuards.cs`): anything a browser
  labels `Sec-Fetch-Site: cross-site` or `same-site` is refused, and requests
  that change data must carry `X-DataMind-Csrf: 1` and, if they send an
  `Origin`, come from this one.
- **Headers** on every response: a strict Content-Security-Policy,
  `X-Frame-Options: DENY`, `nosniff`, `no-referrer`, a restrictive
  `Permissions-Policy`, and `Cache-Control: no-store` on `/api`.

Not built: email verification, password reset, multi-factor authentication.

## Swagger

`http://localhost:5180/swagger` — the API's root redirects there in development.
The document behind it is `http://localhost:5180/openapi/v1.json`.

It is generated from the endpoints: the `WithName`, `WithSummary` and `WithTags`
each route declares are what the page shows, so the reference cannot drift from
the routes. Operations are live. Sign in first — **Try it out** on
`POST /api/auth/login`, or in the app in the same browser — and the session
cookie goes with every call; Swagger adds the CSRF header itself.

Both are **development-only**. A browsable, executable API is a good thing to
have while building and an open door in production, so `MapOpenApi` and
`UseSwaggerUI` sit inside the `IsDevelopment` branch in `Program.cs`.

## Test it

```bash
bash smoke-test.sh
```

Exercises all 107 cases, asserting the status code of each. It signs up two
throwaway accounts: one does the work — every endpoint, its failure paths, an
upload's whole round trip, a report whose source file has since been deleted,
the assistant provider switch — and the other proves it cannot see or change
any of it. Sign-up, sign-in, sign-out, refused cross-site requests and account
deletion are covered too. Both accounts are deleted at the end with everything
they created. Last run: **107 passed, 0 failed**.

Sign-in is rate limited, so leave a minute between runs.

## The idea this is built around

A language model is unreliable at arithmetic, so it never does any here. A
question moves through three stages:

1. **Plan** — `IQuestionPlanner` reads the question and returns a `QuerySpec`:
   what to group by, what to measure, how to aggregate, how to filter.
2. **Execute** — `QueryEngine` runs that spec against the stored rows. This is
   the only place in the codebase a figure is ever produced.
3. **Explain** — the explanation is written from the computed figures, so the
   prose can only state numbers the engine returned.

Three planners ship. `KeywordPlanner` uses keyword rules and no model at all;
`GeminiPlanner` and `OpenRouterPlanner` ask a hosted model for the plan and the
prose. Which one runs is decided per request by `PlannerResolver`, from the
provider the signed-in account saved — so the choice on the Settings screen
actually changes what answers questions.

Every answer carries its working: the spec that ran, the planner that produced
it, the rows scanned, the rows matched after filtering, and the milliseconds
each stage took.

## The assistant

Three providers, chosen per account on the Settings screen and enforced per
request by `PlannerResolver`:

| Provider | What it needs | What it does |
| --- | --- | --- |
| Built-in planner | nothing | Keyword rules pick the query; the answer is templated |
| OpenRouter | `OPENROUTER_API_KEY` | One key, several free models; plans the query and writes the answer |
| Google Gemini | `GEMINI_API_KEY` | The same two jobs |

The `ApiKey` fields in `appsettings*.json` are empty by design: a key in a file
is a key that leaks the moment the folder is shared or committed. Use the
environment variables, or

```bash
dotnet user-secrets set "OpenRouter:ApiKey" "sk-or-v1-..."
```

and rotate any key that has ever been shared at <https://openrouter.ai/keys>.

### What the models are asked for, and what is done with the answer

Both stages are JSON: the plan is a `QuerySpec`, the explanation is one `answer`
field. Everything that comes back is validated — `SpecValidator` drops unknown
columns, operators and aggregates and clamps limits, and an explanation that
reads as a model thinking out loud, echoes the instruction, or arrives as raw
JSON is discarded in favour of the templated answer. **No failure reaches the
screen**: no key, a rate limit, a timeout, a truncated reply and a malformed
plan all fall back to the built-in planner, and the figures are computed by the
engine either way. The response names the planner that actually produced the
spec, so a fallback is never presented as the model's work.

The free models are what a free-tier key can reach, and they vary. Planning is
usually right, including filters — "how many orders are in processing" plans a
count with `status = Processing`, because the prompt carries the values the
loaded file actually holds (`SchemaSummary`). Prose is less reliable, and when
it is not usable the templated writer takes over, which is why an answer
sometimes reads more plainly than the model could have written.

## Gemini

Out of the box the assistant works with **no API key**. The built-in planner
answers every question; the wording is templated rather than written.

To use Gemini instead:

1. Get a free key at <https://aistudio.google.com/apikey>.
2. Make it available to the API — pick one:
   ```bash
   set GEMINI_API_KEY=your-key-here      # cmd, current window
   ```
   ```bash
   dotnet user-secrets set "Gemini:ApiKey" "your-key-here"   # kept out of the repo
   ```
3. Start the API, sign in, then choose **Google Gemini** in Settings.

`GET /api/status` (signed in) reports `gemini: configured` or `gemini: no key`,
and the Settings screen shows Connected or Needs key accordingly.

### What is sent to Google

The question, the column names and their allowed values, and — for the
explanation — the figures the engine already computed. **Your rows are never
sent.** Gemini decides *what* to compute and describes the result; it never sees
the data or does the arithmetic.

### When Gemini fails

Nothing breaks. A missing key, a rejected key, an exhausted quota or a timeout
is logged and the request falls back to the built-in planner, still returning
200 with a correct answer. Everything Gemini returns is validated first — unknown
columns, operators and aggregates are dropped and limits clamped, so a bad plan
never reaches the query engine.

## Layout

```
Program.cs            composition root: accounts, the session cookie, rate
                      limits, the request pipeline and the endpoint groups
Security/             RequestGuards (response headers, cross-site checks) and
                      SecurityRules (password, lockout and input limits)
Endpoints/            one file per screen area, AuthEndpoints for accounts, and
                      Problems.cs for the ProblemDetails wording every failure shares
Query/                QuerySpec, the engine, the shared row filters, the planners
                      and SavedSpecs — the specs the product ships with
Services/             CurrentUser (the signed-in account), DatasetContext (which
                      file a request reads from), CsvProfiler, RowMapper,
                      KpiBuilder, ReportComposer
Data/                 AppDbContext with the per-account filters; its SQLite and
                      PostgreSQL context types and their Migrations; Database.cs,
                      which migrates on startup; DatabaseConnection, which reads
                      the provider from the connection string
Models/               entities and AppUser
Contracts/            the DTOs the API returns
```

Filtering and ordering live once, in `Query/RowFilters.cs`, and both the query
engine and the explorer grid call it — the two screens cannot drift on what a
filter means. The same rule applies to the prompts (`Query/Prompts.cs`) and the
validation of what a model returns (`Query/SpecValidator.cs`): every planner
shares both, so a new provider inherits the guard rails rather than reinventing
them.

## Endpoints

Everything except `/api/health` and `/api/auth/signup`, `/login` and `/logout`
needs a signed-in session, and answers 401 without one.

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/api/health` | Liveness only; public, and says nothing about what is stored |
| POST | `/api/auth/signup` | Create an account and start a session |
| POST | `/api/auth/login` | Start a session |
| POST | `/api/auth/logout` | End the session |
| GET | `/api/auth/me` | The signed-in account |
| DELETE | `/api/auth/account` | Delete the account and everything it owns; needs its password |
| GET | `/api/status` | What this account has loaded, and which planner will answer next |
| GET | `/api/datasets` | Paged, searchable, sortable list |
| GET | `/api/datasets/{id}` | One dataset with its full column profile |
| POST | `/api/datasets/upload` | Upload a delimited file (up to 25 MB); profiles it, stores its rows, reports the field mapping |
| DELETE | `/api/datasets/{id}` | Delete a dataset with its profile and its rows |
| GET | `/api/explorer/columns` | Grid column definitions |
| GET | `/api/explorer/rows` | Paged rows; sorting and repeatable filters |
| GET | `/api/dashboard` | KPI strip, trend, regional split, category bars, insights |
| GET | `/api/analytics` | Aggregate figures for the file in context |
| GET | `/api/chat/sessions` | Conversations, most recent first |
| POST | `/api/chat/sessions` | Start a conversation |
| GET | `/api/chat/sessions/{id}` | Turns, each with its spec and figures |
| PUT | `/api/chat/sessions/{id}` | Save a conversation under a name |
| DELETE | `/api/chat/sessions/{id}` | Delete a conversation |
| POST | `/api/chat/sessions/{id}/ask` | Plan, compute, explain — all three returned |
| GET | `/api/chat/sessions/{id}/stream` | The same, as server-sent events |
| POST | `/api/query/run` | Run an edited `QuerySpec` directly |
| GET | `/api/reports` · `/api/reports/{id}` | Reports, composed from live figures; `410` once the source file is deleted |
| POST | `/api/reports` | Write a report for a dataset |
| DELETE | `/api/reports/{id}` | Delete a report |
| GET | `/api/settings` · PUT · GET `/api/providers` | The planner and model that answer this account's questions, and the providers on offer |
| GET | `/swagger` · `/openapi/v1.json` | Swagger UI and its document (Development only) |

### Filters

Repeatable on `/api/explorer/rows`:

```
?filter=revenue:gt:10000&filter=region:eq:North
```

Operators: `gt gte lt lte eq ne` (or the symbols `> >= < <= = !=`). An unknown
column or a malformed filter returns 400 naming the columns that do exist.

### Streaming

`/api/chat/sessions/{id}/stream` emits `plan`, then `figures`, then a run of
`token` events, then `done` — so a client can render the spec and the computed
figures before the sentence has finished arriving.

## Notable behaviour

- **Accounts cannot see each other's data**, and that is enforced in the
  database context rather than trusted to each endpoint.
- **The model picker is enforced, not decorative.** `PUT /api/settings` rejects a
  model the chosen provider does not serve, with a 400 naming the ones it does.
- **An upload is queryable immediately.** Its rows are mapped onto the queryable
  schema by header name (`Total Amount` → revenue, `Client Name` → customer, and
  so on) and stored, and the upload response names the header behind every
  field. Nothing that cannot be matched is invented: an unmatched text field
  reads `Unspecified`, a row with no readable date is grouped as `Undated`, and
  revenue with no column of its own falls back to quantity × price.
- **Which dataset a request reads from is resolved against the database**
  (`IDatasetContext`): an id that does not exist, or belongs to another account,
  is a 404 rather than another file's figures.
- **KPI deltas are computed, not baked in.** Each one is the latest period
  against the one before it, taken from the same series the chart is drawn from;
  a series too short to support the comparison carries no delta.
- **Errors are `ProblemDetails`** throughout, with a `detail` that says what to
  do next rather than only what went wrong. A body the binder cannot read is a
  400 naming the likely cause, not a 500 — a caller can tell "fix your request"
  from "retry later".
- **Nothing is seeded at all.** A fresh database holds no rows of any kind, not
  even settings: an account's planner choice is stored the first time it saves
  Settings, and until then it uses the built-in planner. The project ships no
  sample file either.
- **Any dataset can be deleted, including the last one.** The empty state is a
  screen the app is built for, and refusing to delete someone's only file
  trapped their data in the product.
- **The keyword planner reads the file's own values.** `SchemaSummary` supplies
  the regions, categories and statuses the loaded file actually holds, and every
  planner — including the fallback path of the model planners — gets them.
- **Text filters ignore case.** `status:eq:shipped` and `status:eq:Shipped` are
  the same request — a filter that silently matches nothing because of a capital
  letter is indistinguishable from having no data.
- Upload parses delimited text only. XLSX and JSON are rejected with a 415 that
  says so — the UI offers them, the server does not pretend to handle them yet.

## Known limits

- No email verification, password reset or multi-factor authentication. Sign-up
  reveals whether an address is registered; rate limiting is the mitigation.
- A report binds to its source file by name, within its account. Delete a file
  and upload a different one under the same name, and the report reads from the
  new one.
- Grouping is done in memory after filtering in SQL, because the group-by column
  is chosen at runtime. Fine at 14k rows; a dataset in the millions wants raw SQL.
- Row mapping is by header name. A file whose columns are named nothing like
  sales data maps few fields, and the response says which ones were left empty.
- XLSX and JSON are still rejected at upload with a 415; only delimited text is
  parsed.

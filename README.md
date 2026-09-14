# DataMind AI — full stack

An AI data analyst: sign in, upload a file, see it analysed, ask it questions.
Every figure on every screen is computed server-side from stored rows — there is
no mock data anywhere in the app — and each account sees only its own files,
reports and conversations.

- **`frontend/`** — React 18 + Vite 8 + Tailwind CSS 3, built to the DataMind AI
  screens (`stitch_insightiq_ai_data_analyst`).
- **`backend/`** — ASP.NET Core on **.NET 10**, minimal APIs over EF Core —
  SQLite locally, PostgreSQL when hosted — with ASP.NET Core Identity for
  accounts. The schema is created by migrations on first run.

## Run both

```bash
start.cmd
```

Or separately:

```bash
dotnet run --project backend/AnalystAI.Api --urls http://localhost:5180
```

```bash
npm --prefix frontend install && npm --prefix frontend run dev
```

The frontend needs the API: start that one first, or run `start.cmd`, which
opens both. Starting the frontend alone gives "Cannot reach the API" — `npm run
dev` warns about it in the terminal before the browser opens.

- Frontend http://localhost:5173 — opens on **Sign in**; the first time, choose
  **Create an account**
- **Swagger http://localhost:5173/swagger** (or http://localhost:5180/swagger — the
  API's root redirects there in development)
- API health http://localhost:5180/api/health
- OpenAPI document http://localhost:5180/openapi/v1.json

The database is created on first start and **nothing is put in it**: no
accounts, no sample files, no rows, no reports, no conversations. Every figure an
account sees comes from something that account uploaded, which is the claim this
product makes.

Upload a CSV of your own from **Datasets → Drag & drop**. The project ships no
sample file. Delete a file from the Datasets screen and the account is empty
again; delete `backend/AnalystAI.Api/analystai.db` to start over completely,
accounts included.

## Accounts and security

- **Accounts** are an email address and a password, stored by ASP.NET Core
  Identity. Passwords are hashed with PBKDF2 and must be at least 12 characters.
  There are no composition rules: length is what makes a password hard to guess,
  and forced symbols are satisfied predictably.
- **The session** is an `HttpOnly`, `SameSite=Strict` cookie — `Secure` and
  `__Host-` prefixed in production — that ends after 12 hours without use. No
  script on the page can read it, and no token is kept in `localStorage`.
- **Each account sees only its own data.** Every owned table carries the account
  id, and the EF Core context filters every query by the signed-in account and
  stamps it on every insert (`Data/AppDbContext.cs`). An id that belongs to
  another account reads as not found, and the smoke test checks that on
  datasets, rows, dashboards, conversations and reports.
- **Password guessing** is limited twice: 20 sign-in or sign-up attempts a
  minute per address, and an account locks for 15 minutes after 5 failures in a
  row. Signing in to an address with no account takes as long, and says the same
  thing, as a wrong password.
- **Cross-site requests are refused.** The API rejects any request a browser
  labels as coming from another site, and every request that changes data must
  carry an `X-DataMind-Csrf: 1` header, which a form on another site cannot add.
- **Response headers** set a strict Content-Security-Policy — scripts from the
  app's own origin only, which is why the theme bootstrap lives in
  `frontend/public/theme-init.js` rather than inline — plus
  `X-Frame-Options: DENY`, `nosniff`, `no-referrer`, and `Cache-Control:
  no-store` on every API response. HSTS and HTTPS redirection are on outside
  development, and the `Server` header is gone.
- **Limits.** Uploads up to 25 MB, questions up to 1,000 characters, titles up
  to 120. The assistant takes 30 questions a minute per account; everything else,
  600 requests a minute.
- **Exports** prefix a cell that starts with `=`, `+`, `-` or `@` with an
  apostrophe, so a crafted value in an uploaded file cannot run as a formula in
  whoever opens the spreadsheet.

Not built yet, and worth knowing before real users arrive:

- **No email verification and no password reset.** There is no email service. A
  forgotten password means a new account, and the sign-up page says so. Sign-up
  also reveals whether an address is already registered; rate limiting is the
  only mitigation until verification exists.
- **No multi-factor authentication.**

A database created before accounts existed holds rows that belong to no one. On
startup the API moves such a file aside as `analystai.pre-accounts-<time>.db` —
it never deletes it — and creates a fresh one.

## Deploying

One artefact, and it carries no secret.

```bash
npm --prefix frontend run build
dotnet publish backend/AnalystAI.Api -c Release -o publish
```

`publish/` holds the API with the built frontend in `publish/wwwroot`: the
publish step copies `frontend/dist` in, so the API serves the app and the API
from one origin. Run it and open its address.

That one origin is required, not just convenient. The session cookie is
`SameSite=Strict` and the API refuses requests from other sites, so the browser
must reach the app and the API at the same address. The single artefact does
that; so does a frontend host that proxies `/api` to the API, which is how the
Vercel deployment below works.

What the host has to be told:

- **HTTPS.** The session cookie is `Secure` in production, so the site must be
  served over HTTPS — directly, or behind a proxy that terminates TLS.
- **`ConnectionStrings__Default`** — the database. It defaults to a SQLite file
  next to the API, which is fine for one machine and wrong for anything that
  redeploys onto fresh disk.
- **A PostgreSQL connection string, where the disk is wiped on restart.** Given
  one in `ConnectionStrings__Default` — key=value, or a `postgresql://` URL —
  the API uses PostgreSQL instead of SQLite. The keys that encrypt sessions are
  kept in the database either way, so a restart signs nobody out.
- **`ASPNETCORE_URLS`** — the port the API listens on.
- **`OPENROUTER_API_KEY`** or **`GEMINI_API_KEY`** — only if you want a hosted
  model to read the questions. Leave both unset and the built-in planner answers.
- **Forwarded headers, behind proxies.** Rate limits are per client address, and
  behind proxies every request comes from the last one. Set
  `Security__TrustForwardedHeaders=true` and `Security__ForwardLimit` to the
  number of proxies in front of the API, and no more: every extra hop trusted is
  an address a client could supply for itself.
- `Security:AuthRequestsPerMinute` — optional, default 20.

The database is created by migrations on first start and holds nothing until
someone signs up.

### Frontend on Vercel, API on Render

Vercel serves the static frontend and proxies `/api` to the API, so the browser
still sees one origin. The API runs on Render as a free Docker web service, and
its data lives in a free PostgreSQL database on Neon.

| Part | Where |
|---|---|
| Frontend | https://datamind-ai-nine.vercel.app (Vercel project `datamind-ai`) |
| API | https://datamind-api-a908.onrender.com (Render service `datamind-api`, Singapore) |
| Database | Neon project, AWS Asia Pacific (Singapore) |

Why Neon and not a Render database: Render's free database needs a card on file
and is deleted 30 days after it is created. Neon's free tier does not expire.
Render still asks for a card to verify the account before it creates even a free
web service; it places a temporary $1 authorization and charges nothing.

To set it up from scratch:

1. **Push this folder to a GitHub repository.** Render deploys from Git. A
   public repository can be deployed without connecting GitHub to Render.
2. **Create the database.** In Neon, create a project in the Singapore region.
   Under **Connect**, turn **Connection pooling** off and copy the connection
   string (`postgresql://...?sslmode=require`). Keep it out of the repository.
3. **Create the API.** Either apply `render.yaml` in Render (**New → Blueprint**),
   which asks for the connection string, or create a **New → Web Service** by
   hand with the same settings:
   - Runtime **Docker**, plan **Free**, region **Singapore**
   - Dockerfile path `./backend/AnalystAI.Api/Dockerfile`, Docker build context
     `./backend/AnalystAI.Api`, health check path `/api/health`
   - Environment: `PORT=8080`, `ConnectionStrings__Default=<Neon string>`,
     `Security__TrustedOrigins__0=https://datamind-ai-nine.vercel.app`,
     `Security__TrustForwardedHeaders=true`, `Security__ForwardLimit=2`, and
     optionally `OPENROUTER_API_KEY` or `GEMINI_API_KEY`

   The live service was created by hand, so `render.yaml` does not manage it:
   change its settings in the Render dashboard. Migrations create the tables on
   first start.
4. **Point Vercel at the API.** `frontend/vercel.json` rewrites `/api/*` to the
   service's `onrender.com` address, before the catch-all. Change it if the
   service address changes.
5. **Deploy the frontend** from `frontend/`, signed in to the Vercel account that
   owns the project (run `npx vercel link` first if it asks which project):
   ```bash
   npx vercel deploy --prod
   ```
   Or run `npx vercel git connect` once, and every push deploys it.

Pushes do not redeploy either half on their own. The live Render service was
created from the repository's public URL, so Render is never told about a push,
even though its Auto-Deploy setting reads "On Commit". After a change to
`backend/`, use **Manual Deploy → Deploy latest commit** in the service, or
connect GitHub to Render (**Account settings → Git**) for automatic deploys. The
frontend likewise deploys only with `npx vercel deploy --prod`, unless Git is
connected in Vercel.

To check the deployment, `https://datamind-ai-nine.vercel.app/api/health` should
return `{"status":"ok"}`: that response comes from Render, through Vercel.

What the free plans mean for this app:

- **The service sleeps** after 15 minutes without traffic and takes about a
  minute to wake. The first request after a quiet spell can show "Cannot reach
  the API"; Try again recovers once it is up. To prevent that,
  `.github/workflows/keep-api-awake.yml` requests `/api/health` every 5 minutes
  from GitHub Actions. GitHub can start scheduled runs late, so an occasional
  sleep can still slip through; an uptime monitor such as UptimeRobot pinging
  the same URL is more punctual. Disable the workflow in the Actions tab to let
  the service sleep again.
- **There is no persistent disk**, which is why the API uses PostgreSQL there:
  anything written to the service's own filesystem is lost when it sleeps.
- **Neon's free tier** suspends an idle database and resumes it on the next
  connection, which adds a moment to the first request after a quiet spell.
- **Changing the database password** in Neon means updating
  `ConnectionStrings__Default` in the Render service's Environment tab, which
  redeploys it.

`frontend/vercel.json` also serves every app route from `index.html`, keeps
`/api` out of that rule, and sends the same Content-Security-Policy and security
headers the API does.

## See the API

Swagger UI is served by the API in development and proxied by the frontend, so
the same link works from either origin. Operations are grouped by the tag each
endpoint declares — Accounts, Datasets, Explorer, Insights, Chat, Reports,
Settings, Health — and the summaries come from the routes themselves rather than
from a separate document that can drift.

Everything except health and the account endpoints needs a session. Sign in in
the same browser first — in the app, or with **Try it out** on
`POST /api/auth/login` — and the cookie goes along with every call. Swagger adds
the `X-DataMind-Csrf` header itself.

The status popover behind the bell in the top bar links to it.

## Check the API

```bash
bash backend/smoke-test.sh
```

107 cases. The run signs up two throwaway accounts — one that does the work, one
that proves it cannot see any of it — and covers every endpoint and its failure
paths: sign-up, sign-in and sign-out, refused cross-site requests, an upload's
full round trip (profile → stored rows → queryable → deletable), report writing
and deletion, a report whose source file has since been deleted, saving a
conversation, the assistant provider switch, isolation between the two
accounts, and deleting an account. Both accounts are deleted at the end with
everything they created. Last run: **107 passed, 0 failed**, leaving no
accounts, files, rows, reports or conversations behind.

Sign-in and sign-up are rate limited, so a second run straight after the first
can be refused with 429. Leave a minute between runs, or raise
`Security:AuthRequestsPerMinute` for the test.

## Check the wiring

```bash
bash link-audit.sh
```

Calls every method in `frontend/src/lib/api.js` against the **frontend's** origin,
so the Vite proxy hop is part of the test — a renamed route, a broken proxy or a
changed status code shows up as a `BAD` line instead of as a blank screen. It
signs up its own account, works through it, and deletes it. Last run: **33
linked, 0 broken**, including the SSE stream, both Swagger routes, and signing
out and back in.

## How it fits together

A language model is unreliable at arithmetic, so it never does any here. A
question moves through three stages: **plan** (a `QuerySpec` chosen by a model or
by keyword rules), **execute** (`QueryEngine`, the only place a figure is ever
produced), **explain** (prose written from the computed figures). Every answer
carries the spec that ran, the rows scanned and the milliseconds each stage took,
and the assistant screen shows all of it.

A new account uses the **built-in planner**, which needs no key and no network:
keyword rules pick the query and the figures are computed the same way they are
everywhere else. Nothing here has to be configured to work.

The app has no screen for choosing a hosted model. An account can still be
switched to one through the API — `PUT /api/settings` with a `providerId`
(`gemini` or `openrouter`) and a `modelName` from `GET /api/providers` — and the
provider needs a key through the environment:

```bash
setx OPENROUTER_API_KEY "your-key-here"
```

```bash
export OPENROUTER_API_KEY="your-key-here"
```

`GEMINI_API_KEY` works the same way. Both are read at startup, so restart the
API after setting one. The `ApiKey` fields in `appsettings*.json` are read too
and are deliberately left empty: a key written into a file leaks the moment the
folder is shared, and `appsettings.Development.json` is in `.gitignore` for that
reason. If a key has ever sat in one of those files, rotate it —
<https://openrouter.ai/keys> for OpenRouter,
<https://aistudio.google.com/apikey> for Gemini.

The model is never load-bearing. With no key, a rate limit, or a model that
returns nonsense, the built-in planner answers instead and every screen keeps
working. Each answer from the chat stream still records which planner produced
it, in the `plan` event's `planner` field.

The same engine backs the rest of the product: Dashboard, Analytics and the
report writer are saved `QuerySpec`s (`Query/SavedSpecs.cs`) run through it, and
the Analytics toolbar builds a spec from its selectors and posts it to
`/api/query/run`.

```
design/                    the Stitch export the UI was built from — one folder
                           per screen with its comp and generated markup

frontend/public
  theme-init.js            sets the theme class before first paint
frontend/src
  context/AuthContext.jsx  the signed-in account: sign in, sign up, sign out
  context/AppContext.jsx   active dataset + the top bar's page actions
  lib/api.js               the only place the app talks to the service
  components/              rail, top bar, panels, charts (inline SVG), auth layout
  pages/                   one file per screen, plus SignIn and SignUp

backend/AnalystAI.Api
  Program.cs               composition root: accounts, session cookie, rate
                           limits, the request pipeline
  Security/                response headers, cross-site checks, input limits
  Endpoints/               one file per screen area, AuthEndpoints, and the
                           shared ProblemDetails
  Query/                   QuerySpec, engine, shared row filters, planners
  Services/                current account, dataset context, CSV profiler,
                           row mapper, KPIs, report composer
  Data/                    EF Core context with per-account filters, migrations,
                           startup
  Models/                  entities and the account
```

Each folder has its own README with the detail: `frontend/README.md` for the
design tokens and screen-by-screen sources, `backend/README.md` for the
endpoints, the Gemini setup and what is sent to Google (questions and column
names — never your rows).

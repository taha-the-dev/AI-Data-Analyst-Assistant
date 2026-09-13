# DataMind AI — frontend

React implementation of the **DataMind AI** screens (Stitch export
`stitch_insightiq_ai_data_analyst`: dashboard, AI assistant, datasets,
analytics, reports, history), built in React + Tailwind CSS against the .NET API
in `../backend`.

## Run it

This reads from the API. Start that first:

```bash
dotnet run --project ../backend/AnalystAI.Api --urls http://localhost:5180
```

Then the frontend:

```bash
npm install
npm run dev
```

Open http://localhost:5173 and sign in, or create an account the first time.
Vite proxies `/api`, `/swagger` and `/openapi` to `http://localhost:5180`, so the
browser only ever sees one origin — which the session requires: it is a
`SameSite=Strict` cookie, so the app and the API must share an origin. Point the
proxy elsewhere with `VITE_API_TARGET`; `VITE_API_BASE` only changes the path the
API is mounted at.

If the API is not running, `npm run dev` says so before the browser opens (the
`predev` preflight, also runnable as `npm run check-api`), the proxy answers with
ProblemDetails instead of a stack trace, and the app shows "Cannot reach the
API" with a Try again button that recovers in place.

```bash
npm run build     # production build into dist/
npm run preview   # serve the production build
```

## Stack

React 18 · Vite 8 · Tailwind CSS 3 · React Router 7. No charting library — the
charts are inline SVG and flex boxes, drawn to the design's own spec.

## Screens

| Route | Screen | Reads from |
| --- | --- | --- |
| `/login` | Sign in | `/api/auth/login` |
| `/signup` | Create an account | `/api/auth/signup` |
| `/` | Dashboard — KPI strip, revenue trend, category split, top products, insights | `/api/dashboard`, `/api/analytics`, `/api/query/run` |
| `/analyst` | AI Assistant — conversation left, analysis panel right | `/api/chat/*` (SSE) |
| `/datasets` | Datasets — dropzone, library, file detail with a live row preview | `/api/datasets/*`, `/api/explorer/rows` |
| `/analytics` | Analytics — metric/dimension/aggregate toolbar, chart, tabular data | `/api/query/run` |
| `/reports`, `/reports/:id` | Reports directory and reader | `/api/reports/*` |
| `/history` | Analysis history — every stored conversation | `/api/chat/sessions` |
| `/settings` | Settings — account, theme, provider and model | `/api/auth/*`, `/api/settings`, `/api/providers` |
| `/explorer` | Data Explorer — the full grid with filters | `/api/explorer/*` |

The rail carries the seven screens the design shows. Data Explorer is reachable
from the Dashboard and Datasets screens.

## Accounts

Every screen except `/login` and `/signup` needs a signed-in account.
`src/context/AuthContext.jsx` asks the API once on load (`/api/auth/me`) — the
session is an HttpOnly cookie, so asking is the only way to know — and until it
answers the app shows "Checking your session…" rather than a frame of any screen.

- A visitor with no session who opens any screen is sent to **Sign in**, and
  returns to that screen afterwards. The return path comes from router state and
  is only ever a path inside this app.
- Any request that comes back 401 — the session expired, or was ended in another
  tab — sends the app back to Sign in.
- **Sign out** sits at the foot of the rail and in **Settings → Account**, which
  also deletes the account after asking for its password. Signing out forgets
  the remembered project, so the next account on the same browser does not
  inherit it.
- The sign-up page checks length and the confirmation before sending, and says
  plainly that there is no password reset yet.

`src/lib/api.js` sends `X-DataMind-Csrf: 1` on every request that changes data.
The API refuses such requests without it.

## The top bar

Every control in it does something, because a control that does nothing is worse
than one that is absent:

- **File tabs** name the projects in the library. Picking one switches what every
  screen below reads from; the choice survives a reload. The Dashboard carries
  the same choice as an explicit **Project** picker, since that is where the
  figures are read.
- **Calendar** reports the period the active file actually covers, read off its
  first and last row by date.
- **Bell** carries the account's service state from `/api/status` — whether the
  API answers, how much this account has loaded, and which planner will answer
  the next question. The dot appears only when something needs attention, which
  means the API being unreachable or the chosen provider having no key.
- **The status popover** also links to the API reference — Swagger, served by the
  API and proxied here, so it opens same-origin.
- **Filter** and **Export** belong to the screen underneath. A page registers its
  handlers (`usePageActions`) and the bar renders them; Export writes a CSV of
  exactly the figures on screen. A screen with nothing to filter or export gets a
  disabled button that says why.

## Design tokens

Sampled from the source screens, then opened up for long sessions. Every colour
is a CSS custom property declared in `src/index.css`; `tailwind.config.js` names
them but holds no hex of its own, so a theme is one block of overrides rather
than a `dark:` variant on several hundred class lists.

- **Rail** `#151C23` base, `#212A33` hover, `#2B343D` current screen — a
  near-black charcoal with a trace of blue, so the actions it frames stay the
  only saturated thing on screen. The sign-in pages use the same charcoal for
  their left half, so signing in reads as stepping into the app.
- **Action** `#2563EB` primary fill, `#0B4ED1` for primary text and chart fills,
  `#1749B8` on hover — a fill hover and a link hover are separate tokens, because
  in dark one has to get darker and the other lighter
- **Surfaces** `#FFFFFF` cards on an `#EDF1F6` canvas. The canvas is dimmed a
  step below the cards so the two separate; white cards on a near-white page put
  the whole screen at full luminance and read as one sheet
- **Status** `#DCFCE7`/`#0F7A38` positive, `#FEE2E2`/`#C42121` negative — both
  darkened from the export, where the chip text sat at 3:1 against its own fill
- **Type** Geist for titles, KPI values and labels; Inter for body
- **Shape** 8px on controls, 12px on panels; one ambient shadow in light, a
  hairline ring in dark, where a drop shadow only muddies the edge it defines
- **Rhythm** 4/8/14/20/28px scale, 228px rail, 52px bar — a console's density,
  not a marketing page's, but not the comp's either: 22px page titles, 14px body
  and a 12px floor, on line-heights near 1.5, with numbers in tabular figures
  doing the talking

Every text colour in the shipped screens clears WCAG AA against the surface
behind it, in both themes.

## Motion

Entrances only, and only where they carry meaning: screens assemble top-down on
arrival (`.stagger`), rows and chat turns fade up as they land, trend lines draw
themselves from the first point to the last, and columns and bars grow from
their axis. Nothing loops, nothing moves on hover except colour, and the whole
system is switched off under `prefers-reduced-motion`.

## Responsive

The rail docks at `md` (768px) and becomes a drawer below it. Beyond that, the
aim is that no screen *loses* a capability on a small viewport: tables become
cards or scroll inside their own container (never the page), row actions do not
depend on hover, and dialogs scroll their own body. The sign-in pages drop their
charcoal half below `lg` and keep the form.

## Interaction

- **Charts are readable.** Hovering a trend line gives a crosshair and the figure
  for that point; columns, bars and donut segments report their own values.
- **Tables sort** where the API sorts, with `aria-sort` on the header.
- **Keyboard.** Skip link, focus trapped in dialogs, focus moved to the new page
  on navigation, and focus rings switch to the accent inside the dark rail where
  the primary blue would be invisible.

## Data

There is no mock data anywhere. Every figure on every screen is computed by the
API from stored rows belonging to the signed-in account.

- `src/lib/api.js` — the only place the app talks to the service. Throws
  `ApiError` carrying the server's ProblemDetails, so screens can show what
  happened *and* what to do about it.
- `src/hooks/useResource.js` — runs an async call and tracks loading, error and
  data, plus a `reload` for retry buttons and post-mutation refresh.
- `src/context/AuthContext.jsx` — the signed-in account.
- `src/context/AppContext.jsx` — the active dataset (shared by every screen) and
  the top bar's page actions.
- `src/lib/csv.js` — the Export button, writing out the figures on screen. A cell
  that starts with `=`, `+`, `-` or `@` gets a leading apostrophe, so a value
  from an uploaded file cannot run as a spreadsheet formula.
- `src/components/ui.jsx` — the shared vocabulary every screen builds from:
  `Panel`/`PanelHeader` for surfaces, `Button`/`IconButton` for actions,
  `StatCard`, `Select`, `SegmentedControl`, `StatusChip`, `Pagination`, and the
  loading and error states. Screens used to hand-write button and panel classes;
  one implementation each means a change to the density or the palette lands
  everywhere at once instead of in the files someone remembered to edit.

`bash ../link-audit.sh` calls every method in `api.js` through this origin and
reports what is and is not wired up.

Where the design shows something this service has no data for, the screen shows
the nearest true thing rather than a plausible invention: the rail's footer names
the planner currently answering questions and the signed-in email, the
assistant's "SQL" block shows the `QuerySpec` the planner actually chose, and the
reports table names the source file where the comp had an owner's avatar.

Nothing is seeded at all: a new account opens on an empty state that asks for a
file, and every screen fills from what it uploads. `NoProject` is that state — an
invitation with the one action that gets past it, not an error, because an empty
library is not a failure.

### The assistant

The provider is chosen per account on the Settings screen and enforced
server-side. When a model is slow, rate-limited or returns something unusable,
the API answers with its built-in planner instead — so the screen never shows an
error in place of an answer, and the analysis panel always has the spec and
figures behind whatever was said.

`/analyst` opens an `EventSource` against the API's SSE endpoint and renders the
three stages as they arrive: the chosen `QuerySpec`, then the computed figures
and their chart, then the sentence streaming in word by word. The analysis panel
on the right holds the results table, the visualisation and the query plan, with
the rows scanned and the compute time — the product's whole claim is that figures
are traceable, so that sits beside the answer rather than behind a menu.

## Notes

- Two themes. The export shipped one palette, so dark is the rail's own charcoal
  expanded to the whole canvas rather than a second palette invented from
  nothing; the rail then drops a step deeper than the page so it still reads as
  the frame. **Settings → Appearance** picks Light, Dark or System. The choice
  is stored per device in `localStorage`, not on the account, and the class is
  stamped on `<html>` by `public/theme-init.js`, a blocking script loaded first
  in `index.html`, before the first paint — anything later shows a white flash
  on the way into a dark session. It is a file rather than inline so the
  Content-Security-Policy can refuse every inline script.
- No third-party image requests: avatars and glyphs are icons or initials.
- Material Symbols loads with `display=block`, so icons stay invisible until the
  font arrives instead of flashing their ligature names as raw text.

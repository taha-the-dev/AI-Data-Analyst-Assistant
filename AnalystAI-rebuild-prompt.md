# Rebuild prompt — AnalystAI

Paste everything below the line into Claude. Attach the repo (or a zip of it) if you can;
if you can't, the prompt stands on its own and Claude can build from scratch.

---

You are the design and engineering lead on **AnalystAI**. I want the next version: a
noticeably better product, not a restyle of the current one. Take a real point of view and
defend it.

## What the product is

AnalystAI is a data analyst that runs on the user's own machine. You upload a CSV or Excel
file; it profiles every column, then you ask questions about the data in plain English and
get an answer with a chart.

**The one idea that matters — do not lose it.** Language models are unreliable at
arithmetic, so this app never lets one do arithmetic. A question goes through three stages:

1. **Plan** — the model reads the column schema and returns a `QuerySpec` (JSON): what to
   group by, what to measure, how to aggregate, how to filter, which chart suits it.
2. **Execute** — server-side code runs that spec against the real rows. Every number the
   user sees is computed here.
3. **Explain** — the model gets the question *and the computed figures* back, and writes the
   answer in sentences. It is told to state only numbers it was given.

So: the model chooses *what* to calculate, the server calculates it, and every answer can
show its working. That provenance — the query that produced a number, the rows scanned — is
the product's whole reason to exist. It should be visible, not buried behind a disclosure.

## Stack

Keep: **C# / ASP.NET Core 8 Web API · Entity Framework Core · SQL Server · React 18 (Vite) ·
Tailwind · Recharts · JWT auth**. The model provider is pluggable behind one interface —
Ollama for a local model, OpenRouter for a hosted one, selected by config.

You may restructure anything inside that stack — schema, endpoints, component architecture,
routing, state management. If you want to add a library, justify it in one line.

## Data model

- `User` → `UserSettings`
- `Dataset` → `DatasetColumn` (name, ordinal, kind, missing/distinct counts, min/max/mean/
  median/stddev, sample values) and `DatasetRow` (one JSON object per row)
- `Analysis` — a saved `QuerySpec` + result, so any answer can be replayed
- `ChatSession` → `ChatMessage` (role, content, chart JSON, spec JSON, latency)
- `Report` — markdown content + the charts it was written from

`QuerySpec` is the contract the whole app runs on: `intent`, `groupBy`, `metric`,
`aggregate` (sum/avg/count/min/max/median), `filters[]` (column, op, value),
`sort`, `limit`, `timeBucket` (day/week/month/quarter/year), `chart`, `title`.

## Pages today

Overview (dashboard), Datasets, Upload, Analysis (tabs: Overview / Data / Statistics /
Charts / Insights), Ask (the assistant), Reports + a report reader, Settings, Login.

Keep the capabilities. Rethink the structure if you have a better one — argue for it.

## Known gaps — fix these

These are real problems in the current build, not hypotheticals:

1. **The model picker is decorative.** Settings saves `UserSettings.ModelName` to the
   database, but inference reads the model name from server config and ignores it. Either
   wire per-user model selection through to the provider, or remove the control. Don't ship
   a setting that does nothing.
2. **No streaming.** An answer takes several seconds and arrives all at once. Stream the
   explanation token by token; show the plan and the computed figures the moment they're
   ready, before the prose finishes.
3. **No conversation memory.** Each question is planned in isolation, so "break that down by
   month" doesn't work. Pass prior turns into the planner.
4. **Chat sessions are invisible.** Sessions and messages are persisted but there's no way
   to browse or resume them. Either build the history UI or stop storing them.
5. **Filters exist in the spec but not in the UI.** `QuerySpec.filters` is honoured by the
   engine; nothing lets a user set one directly.
6. **No cancellation.** An in-flight question can't be stopped.
7. **No error boundaries.** One render error blanks the app.
8. **No tests.** Not one. At minimum, cover the query engine — it computes every number the
   user trusts.
9. **670 KB JS bundle**, no code splitting.
10. **Nothing is exportable** except a report as markdown. No chart PNG, no result CSV.

## What "better" means

Beyond the fixes, I want the product to feel further along. Some directions — pick the ones
you can defend, propose your own:

- Make the working genuinely inspectable: let someone see the spec, edit it, and re-run it.
- Make follow-up questions feel conversational rather than one-shot.
- Do something intelligent on upload — surface what's interesting about the data before
  being asked.
- Treat "I don't know" honestly: when the planner can't map a question to the schema, say so
  and show what the data *can* answer.
- Handle scale: what happens at 500k rows?

## Design

Give it a visual identity that couldn't be mistaken for a generic dashboard template. Ground
the choices in the subject — precision, measurement, provenance, showing your working.

Make deliberate decisions about palette, typography, layout, and motion, and name the one
element the product will be remembered by.

Avoid the defaults that show up in every AI-generated design: cream background with a
high-contrast serif and a terracotta accent; near-black with a single acid-green or vermilion
accent; broadsheet columns with hairline rules and zero border-radius. Any of those is fine
if you *choose* it for a reason and say what the reason is — not fine as a fallback.

Before you build, show me the direction: palette as named hex values, the typefaces and what
each is for, a layout concept, and the signature element. I'll approve it, then you build.

## Quality bar

- Responsive to 375px. No horizontal scrolling; wide tables scroll inside their own
  container, not the page.
- Light and dark, both deliberate.
- Visible keyboard focus, sensible tab order, real labels, `prefers-reduced-motion` honoured.
- Every number monospaced and tabular so columns align.
- Loading, empty, and error states for every surface — an empty screen is an invitation to
  act, and an error says what happened and what to do.
- Copy in plain language, active voice, sentence case. A control names what happens when you
  use it, and keeps that name through the flow.

## How to work

1. Read the codebase (if provided) and tell me what you found — what's solid, what's weak.
2. Propose the design direction and the functional plan. Wait for my approval.
3. Build it. Run it. Verify in a browser: check the console, click through the real flows,
   test at mobile width, screenshot what changed.
4. Report honestly. If something is broken or unfinished, say so plainly. If tests fail,
   show the output. Don't tell me it works if you haven't run it.

Ask me anything that would change what you build. Decide the rest yourself and tell me what
you decided.

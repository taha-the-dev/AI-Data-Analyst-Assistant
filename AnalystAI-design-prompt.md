# Design prompt — AnalystAI

Paste everything below the line. Attach the repo or a screenshot of the current UI if you have one.

---

You are the design lead on **AnalystAI**. I want a new visual design for the whole app —
one that could not be mistaken for a generic dashboard template. Don't touch the
functionality; redesign how all of it looks and feels.

## The product

AnalystAI is a data analyst that runs on your own machine. You upload a CSV or Excel file,
it profiles every column, and you ask questions about the data in plain English. Answers
come back as a sentence plus the chart behind them.

**The idea the design should be built around:** language models are bad at arithmetic, so
this app never lets one do arithmetic. The model decides *what* to compute, server-side code
computes it against the real rows, and the model explains the figures it was handed. That
means every answer can show its working — the query that produced the number, how many rows
were scanned. Provenance is the product's whole reason to exist. Design around that, don't
bury it.

Audience: analysts and developers who'll stare at this for hours. It's a tool, not a
landing page.

## Screens to design

- **Overview** — hero question box, four summary figures, recent datasets table
- **Datasets** — searchable table, per-row actions, delete confirmation
- **Upload** — dropzone, in-progress, success with a profile summary, error
- **Analysis** — one dataset across five tabs: Overview (stat strip + charts), Data (paged
  raw table), Statistics (per-column min/max/mean/stddev), Charts (group-by / measure /
  aggregate builder), Insights (saved results)
- **Ask** — the assistant: question, streamed answer, inline chart, the working underneath
- **Reports** — list, plus a long-form report document with numbered figures
- **Settings** — account, appearance, model provider, data defaults
- **Login**
- **Shared** — nav, dataset switcher, command palette (⌘K), modals, toasts, empty states,
  loading skeletons, charts

## What I want from you

**First, the direction — don't write code yet:**

- **Colour** — 4–6 named hex values, and the rule for when each is used
- **Type** — the faces and what each one is for; make the type treatment itself memorable,
  not a neutral delivery vehicle
- **Layout** — the structural concept, with a rough wireframe
- **Signature** — the one element this app will be remembered by

Then show it to me — a rendered mockup of the most characteristic screen beats a
description. I'll approve or push back, and then you build it.

## Constraints

Grounded in the subject. The vocabulary of precision, measurement, and provenance is where
the distinctive choices live — not in decoration bolted on afterwards.

Avoid the looks that show up in every AI-generated design: cream background with a
high-contrast serif and a terracotta accent; near-black with one acid-green or vermilion
accent; broadsheet columns of hairline rules with zero border-radius. Choose any of them
deliberately and tell me why, or don't use them.

Spend your boldness in one place. Let the signature element be the memorable thing and keep
everything around it quiet. Then cut one more thing than feels comfortable.

## Quality bar

- Responsive down to 375px. No horizontal page scroll — wide tables scroll inside their own
  container.
- Light and dark, both designed on purpose, not one inverted.
- Visible keyboard focus, real labels, `prefers-reduced-motion` respected.
- Every number monospaced and tabular so columns of figures line up.
- Loading, empty, and error states for every surface. An empty screen is an invitation to
  act; an error says what happened and how to fix it.
- Copy in plain language and sentence case. A button names what happens when you press it,
  and keeps that name through the whole flow.

## Then

Build it, run it, and check it in a browser yourself — console clean, real flows clicked
through, mobile width tested. Send me screenshots of what changed. Tell me plainly what you
finished and what you didn't.

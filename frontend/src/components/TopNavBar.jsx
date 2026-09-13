import { useEffect, useRef, useState } from 'react'
import Icon from './Icon'
import { api } from '../lib/api'
import { useDatasets, usePageActionsState } from '../context/AppContext'
import { int } from '../lib/format'

/** Closes a popover on an outside click or Escape. */
function usePopover() {
  const [open, setOpen] = useState(false)
  const ref = useRef(null)

  useEffect(() => {
    if (!open) return
    const onDown = (e) => {
      if (!ref.current?.contains(e.target)) setOpen(false)
    }
    const onKey = (e) => e.key === 'Escape' && setOpen(false)
    document.addEventListener('mousedown', onDown)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onDown)
      document.removeEventListener('keydown', onKey)
    }
  }, [open])

  return { open, setOpen, ref }
}

function Panel({ children, className = '' }) {
  return (
    <div
      className={`absolute right-0 top-[calc(100%+4px)] z-50 w-[260px] rounded-xl border border-outline-variant bg-surface-container-lowest p-md ambient-shadow animate-fade-up ${className}`}
    >
      {children}
    </div>
  )
}

/**
 * The calendar button reports the period the active file actually covers, read
 * off its first and last row by date. There is no date picker behind it because
 * the API has no date-range parameter for it to drive.
 */
function CoverageButton() {
  const { open, setOpen, ref } = usePopover()
  const { activeId, active } = useDatasets()
  const [range, setRange] = useState({ state: 'idle' })

  useEffect(() => {
    if (!open || !activeId) return
    setRange({ state: 'loading' })
    Promise.all([
      api.explorer.rows({ datasetId: activeId, page: 1, pageSize: 1, sort: 'date', dir: 'asc' }),
      api.explorer.rows({ datasetId: activeId, page: 1, pageSize: 1, sort: 'date', dir: 'desc' }),
    ])
      .then(([first, last]) =>
        setRange({
          state: 'ready',
          from: first.items[0]?.date,
          to: last.items[0]?.date,
          rows: first.total,
        })
      )
      .catch((error) => setRange({ state: 'error', error }))
  }, [open, activeId])

  return (
    <div className="relative" ref={ref}>
      <button
        onClick={() => setOpen((v) => !v)}
        aria-label="Period covered by this file"
        aria-expanded={open}
        className="w-7 h-7 flex items-center justify-center rounded-lg text-on-surface-variant hover:bg-surface-container-low transition-colors"
      >
        <Icon name="calendar_month" size={16} />
      </button>

      {open && (
        <Panel>
          <p className="font-label-bold text-label-bold text-on-surface">Period covered</p>
          <p className="font-body-sm text-body-sm text-on-surface-variant mt-xs">
            {active?.name ?? 'No file selected'}
          </p>
          {range.state === 'loading' && (
            <p className="font-body-sm text-body-sm text-on-surface-variant mt-sm">
              Reading first and last row…
            </p>
          )}
          {range.state === 'error' && (
            <p className="font-body-sm text-body-sm text-on-surface-variant mt-sm">
              {range.error?.detail || range.error?.title || 'No dated rows in this file.'}
            </p>
          )}
          {range.state === 'ready' && (
            <dl className="mt-sm grid grid-cols-2 gap-sm">
              {[
                ['From', range.from ?? '—'],
                ['To', range.to ?? '—'],
              ].map(([label, value]) => (
                <div key={label} className="rounded-lg bg-surface-container-low p-sm">
                  <dt className="font-body-sm text-body-sm text-on-surface-variant">{label}</dt>
                  <dd className="font-code text-code text-on-surface tabular-nums mt-[2px]">{value}</dd>
                </div>
              ))}
              <div className="col-span-2 rounded-lg bg-surface-container-low p-sm">
                <dt className="font-body-sm text-body-sm text-on-surface-variant">Rows</dt>
                <dd className="font-code text-code text-on-surface tabular-nums mt-[2px]">
                  {int(range.rows ?? 0)}
                </dd>
              </div>
            </dl>
          )}
        </Panel>
      )}
    </div>
  )
}

/**
 * The bell carries service state rather than invented notifications: whether
 * the API answers, what it has loaded, and whether a model key is configured.
 * The dot appears only when something actually needs attention.
 */
function StatusButton() {
  const { open, setOpen, ref } = usePopover()
  const [health, setHealth] = useState({ state: 'loading' })

  useEffect(() => {
    let cancelled = false
    const check = () =>
      api
        .status()
        .then((data) => !cancelled && setHealth({ state: 'ready', data }))
        .catch((error) => !cancelled && setHealth({ state: 'error', error }))

    check()
    const timer = setInterval(check, 30000)
    return () => {
      cancelled = true
      clearInterval(timer)
    }
  }, [])

  // The dot means "something needs you", so it tracks whether the API answers
  // and whether the selected assistant is actually usable — not whether some
  // provider nobody chose has a key.
  const needsAttention = health.state === 'error' || health.data?.assistant?.ready === false

  return (
    <div className="relative" ref={ref}>
      <button
        onClick={() => setOpen((v) => !v)}
        aria-label="Service status"
        aria-expanded={open}
        className="w-7 h-7 flex items-center justify-center rounded-lg text-on-surface-variant hover:bg-surface-container-low transition-colors relative"
      >
        <Icon name="notifications" size={16} />
        {needsAttention && (
          <span className="absolute top-1.5 right-1.5 w-2 h-2 rounded-full bg-danger border border-surface-container-lowest" />
        )}
      </button>

      {open && (
        <Panel>
          <p className="font-label-bold text-label-bold text-on-surface">Service status</p>
          {health.state === 'loading' && (
            <p className="font-body-sm text-body-sm text-on-surface-variant mt-sm">Checking…</p>
          )}
          {health.state === 'error' && (
            <p className="font-body-sm text-body-sm text-on-surface-variant mt-sm">
              {health.error?.detail || 'The API is not responding.'}
            </p>
          )}
          {health.state === 'ready' && (
            <ul className="mt-sm flex flex-col gap-sm">
              {[
                ['API', health.data.status === 'ok' ? 'Responding' : health.data.status],
                ['Datasets loaded', int(health.data.datasets)],
                ['Rows stored', int(health.data.rows)],
                ['Assistant', health.data.assistant?.provider ?? '—'],
                ['Model', health.data.assistant?.ready ? health.data.assistant.model : 'Built-in fallback'],
              ].map(([label, value]) => (
                <li key={label} className="flex items-center justify-between gap-md">
                  <span className="font-body-sm text-body-sm text-on-surface-variant shrink-0">{label}</span>
                  {/* Model slugs run long; the full value stays available on hover. */}
                  <span
                    title={String(value)}
                    className="font-code text-code text-on-surface tabular-nums truncate text-right"
                  >
                    {value}
                  </span>
                </li>
              ))}
            </ul>
          )}

          {/* Every screen here is a view of the API; this is the API itself. */}
          <a
            href="/swagger"
            target="_blank"
            rel="noreferrer"
            className="mt-md flex items-center gap-sm rounded-lg border border-outline-variant px-sm py-[6px] font-label-bold text-label-bold text-primary hover:bg-surface-container-low transition-colors"
          >
            <Icon name="api" size={16} />
            Open the API reference
            <Icon name="open_in_new" size={14} className="ml-auto text-on-surface-variant" />
          </a>
        </Panel>
      )}
    </div>
  )
}

/**
 * Docked bar. Names the files in the library as tabs — picking one switches
 * what every screen below reads from — and carries the two page-level actions
 * the current screen has registered.
 */
export default function TopNavBar({ onOpenNav }) {
  const { datasets, activeId, setActiveId, loading, error } = useDatasets()
  const { actions } = usePageActionsState()

  return (
    <header className="bg-surface-container-lowest h-topbar w-full sticky top-0 z-30 border-b border-outline-variant flex items-center shrink-0">
      <div className="flex items-center gap-md w-full px-md md:px-lg max-w-content-max mx-auto">
        <button
          onClick={onOpenNav}
          aria-label="Open navigation"
          className="md:hidden p-1.5 -ml-1 rounded text-on-surface hover:bg-surface-container-low transition-colors shrink-0"
        >
          <Icon name="menu" size={18} />
        </button>

        {/* File tabs — the row of dataset names from the design, made to switch context. */}
        <div
          className="flex items-center gap-md h-topbar min-w-0 overflow-x-auto no-scrollbar"
          role="tablist"
          aria-label="Active dataset"
        >
          {loading && <span className="font-body-main text-body-main text-outline">Loading files…</span>}
          {error && <span className="font-body-main text-body-main text-error">API unreachable</span>}
          {datasets.map((d) => {
            const active = d.id === activeId
            return (
              <button
                key={d.id}
                role="tab"
                aria-selected={active}
                onClick={() => setActiveId(d.id)}
                className={`h-full border-b-2 whitespace-nowrap transition-colors ${
                  active
                    ? 'border-primary-container text-primary font-label-bold text-label-bold'
                    : 'border-transparent text-on-surface-variant font-body-main text-body-main hover:text-on-surface'
                }`}
              >
                {d.name}
              </button>
            )
          })}
        </div>

        <div className="flex items-center gap-sm ml-auto shrink-0">
          <CoverageButton />
          <StatusButton />

          <div className="w-px h-6 bg-outline-variant mx-1 hidden sm:block" />

          <button
            onClick={actions.onFilter}
            disabled={!actions.onFilter}
            title={
              actions.onFilter
                ? (actions.filterLabel ?? 'Filter this screen')
                : 'This screen has nothing to filter'
            }
            className="inline-flex items-center gap-sm h-[30px] px-sm rounded-lg border border-outline-variant bg-surface-container-lowest font-label-bold text-label-bold text-on-surface hover:bg-surface-container-low transition-colors disabled:opacity-40 disabled:hover:bg-surface-container-lowest"
          >
            <Icon name="filter_list" size={15} />
            {/* This is the only way to filter a screen, so it cannot disappear
                on a narrow one. The label goes; the control stays. */}
            <span className="hidden sm:inline">Filter</span>
          </button>

          <button
            onClick={actions.onExport}
            disabled={!actions.onExport}
            title={
              actions.onExport
                ? (actions.exportLabel ?? 'Export what this screen is showing')
                : 'This screen has nothing to export'
            }
            className="inline-flex items-center gap-sm h-[30px] px-sm rounded-lg bg-primary-container text-white font-label-bold text-label-bold hover:bg-primary-hover transition-colors disabled:opacity-40 disabled:hover:bg-primary-container"
          >
            <Icon name="download" size={15} />
            Export
          </button>
        </div>
      </div>
    </header>
  )
}

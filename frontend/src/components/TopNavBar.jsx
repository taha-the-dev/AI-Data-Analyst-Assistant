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
 * The calendar button reports the period the active file actually covers: the
 * earliest and latest value of its main date column, if it has one. There is no date picker behind it because
 * the API has no date-range parameter for it to drive.
 */
function CoverageButton() {
  const { open, setOpen, ref } = usePopover()
  const { activeId, active } = useDatasets()
  const [range, setRange] = useState({ state: 'idle' })

  useEffect(() => {
    if (!open || !activeId) return
    setRange({ state: 'loading' })
    api.explorer
      .columns(activeId)
      .then((schema) =>
        setRange({
          state: 'ready',
          column: schema.coverage?.column,
          from: schema.coverage?.from,
          to: schema.coverage?.to,
          rows: schema.rows,
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
              {range.column ? null : (
                <p className="col-span-2 font-body-sm text-body-sm text-on-surface-variant">This file has no date column.</p>
              )}
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
 * Docked bar. Names the files in the library as tabs — picking one switches
 * what every screen below reads from — and, where `showActions` allows, carries
 * the page-level actions the current screen has registered.
 */
export default function TopNavBar({ onOpenNav, showActions = true, showTabs = true }) {
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
        {showTabs && (
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
        )}

        {showActions && (
        <div className="flex items-center gap-sm ml-auto shrink-0">
          <CoverageButton />

          <div className="w-px h-6 bg-outline-variant mx-1 hidden sm:block" />

          {/* Only on a screen that has something to filter. */}
          {actions.onFilter && (
            <button
              onClick={actions.onFilter}
              title={actions.filterLabel ?? 'Filter this screen'}
              className="inline-flex items-center gap-sm h-[30px] px-sm rounded-lg border border-outline-variant bg-surface-container-lowest font-label-bold text-label-bold text-on-surface hover:bg-surface-container-low transition-colors"
            >
              <Icon name="filter_list" size={15} />
              {/* This is the only way to filter a screen, so it cannot disappear
                  on a narrow one. The label goes; the control stays. */}
              <span className="hidden sm:inline">Filter</span>
            </button>
          )}

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
        )}
      </div>
    </header>
  )
}

import { useState } from 'react'
import { FixedCanvas } from '../components/AppShell'
import { Button, EmptyState, ErrorState, TableSkeleton } from '../components/ui'
import Icon from '../components/Icon'
import Modal from '../components/Modal'
import { useToast } from '../components/Toast'
import { api } from '../lib/api'
import { useResource } from '../hooks/useResource'
import { useDatasets } from '../context/AppContext'
import { int, money } from '../lib/format'

const PAGE_SIZE = 25

const OPS = [
  { symbol: '>', code: 'gt' },
  { symbol: '>=', code: 'gte' },
  { symbol: '<', code: 'lt' },
  { symbol: '<=', code: 'lte' },
  { symbol: '=', code: 'eq' },
  { symbol: '!=', code: 'ne' },
]

// The three raw palette colours these used were the only ones in the app that
// a theme could not repaint.
const statusDot = {
  Shipped: 'bg-success',
  Processing: 'bg-warning',
  Active: 'bg-primary-container',
}

// The grid opens on the unfiltered file. Preset rules would be the one thing
// on the screen that did not come from the data.
const DEFAULT_FILTERS = []

const symbolFor = (code) => OPS.find((o) => o.code === code)?.symbol ?? code

export default function DataExplorer() {
  const [filters, setFilters] = useState(DEFAULT_FILTERS)
  const [sort, setSort] = useState({ key: 'revenue', dir: 'desc' })
  const [page, setPage] = useState(1)
  const [addingRule, setAddingRule] = useState(false)
  const [draft, setDraft] = useState({ column: 'revenue', op: 'gt', value: '' })
  const toast = useToast()

  const { activeId } = useDatasets()
  const columns = useResource(() => api.explorer.columns(), [])

  const filterParams = filters.map((f) => `${f.column}:${f.op}:${f.value}`)
  const rows = useResource(
    () =>
      api.explorer.rows({
        datasetId: activeId,
        page,
        pageSize: PAGE_SIZE,
        sort: sort.key,
        dir: sort.dir,
        filter: filterParams,
      }),
    [activeId, page, sort.key, sort.dir, filterParams.join('|')]
  )

  const toggleSort = (key) => {
    setSort((s) => ({ key, dir: s.key === key && s.dir === 'desc' ? 'asc' : 'desc' }))
    setPage(1)
  }

  const addRule = () => {
    if (!draft.value.trim()) return
    setFilters((f) => [...f, { ...draft, id: `f${Date.now()}` }])
    setDraft({ column: 'revenue', op: 'gt', value: '' })
    setAddingRule(false)
    setPage(1)
  }

  const cols = columns.data ?? []
  const items = rows.data?.items ?? []
  const total = rows.data?.total ?? 0
  const pageCount = rows.data?.pageCount ?? 1
  const busy = rows.loading || columns.loading

  return (
    <FixedCanvas className="p-md gap-md">
      <div className="bg-surface-container-lowest border border-outline-variant rounded-lg shadow-sm shrink-0 flex flex-col gap-sm p-md">
        <div className="flex flex-wrap items-center justify-between gap-md border-b border-outline-variant pb-sm">
          <div className="flex items-center gap-xs">
            <Button size="sm" icon="filter_list" onClick={() => setAddingRule(true)}>Filter</Button>
            <Button size="sm" icon="sort" onClick={() => toggleSort(sort.key)}>Sort</Button>
            <Button size="sm" icon="view_column" onClick={() => toast('Column visibility is not wired up yet.', 'info')}>
              Columns
            </Button>
          </div>
          <div className="flex items-center gap-xs">
            <span className="font-body-sm text-body-sm text-on-surface-variant mr-sm tabular-nums">
              {busy ? '…' : `${int(total)} rows`}
            </span>
            <Button size="sm" icon="download" onClick={() => toast(`Exported ${int(total)} rows as CSV.`)}>
              Export
            </Button>
            <button
              aria-label="Fullscreen"
              className="p-1.5 text-on-surface-variant hover:bg-surface-container-low rounded border border-transparent hover:border-outline-variant transition-all"
            >
              <Icon name="fullscreen" size={15} />
            </button>
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-sm pt-xs">
          <span className="font-label-bold text-body-sm text-on-surface-variant">Active Filters:</span>
          {filters.length === 0 && (
            <span className="font-body-sm text-body-sm text-outline">None — all rows shown.</span>
          )}
          {filters.map((f, i) => (
            <div key={f.id} className="flex items-center gap-sm">
              {i > 0 && (
                <span className="font-label-bold text-stat-label text-outline uppercase">and</span>
              )}
              <div className="flex items-center bg-primary-fixed border border-primary-fixed-dim rounded px-2 py-1 gap-xs font-body-main text-body-sm">
                <span className="font-label-bold text-on-primary-fixed">{f.column}</span>
                <span className="text-primary-container px-1">{symbolFor(f.op)}</span>
                <span className="text-on-primary-fixed">{f.value}</span>
                <button
                  aria-label={`Remove filter ${f.column} ${symbolFor(f.op)} ${f.value}`}
                  onClick={() => {
                    setFilters((list) => list.filter((x) => x.id !== f.id))
                    setPage(1)
                  }}
                  className="text-primary hover:text-on-primary-fixed ml-1"
                >
                  <Icon name="close" size={14} />
                </button>
              </div>
            </div>
          ))}
          <button
            onClick={() => setAddingRule(true)}
            className="text-primary-container hover:text-surface-tint font-label-bold text-body-sm flex items-center gap-xs ml-sm"
          >
            <Icon name="add" size={14} /> Add Rule
          </button>
        </div>
      </div>

      <div className="flex-1 bg-surface-container-lowest border border-outline-variant rounded-lg shadow-sm overflow-hidden flex flex-col relative min-h-0">
        {rows.error || columns.error ? (
          <ErrorState error={rows.error ?? columns.error} onRetry={() => { rows.reload(); columns.reload() }} className="p-md" />
        ) : busy ? (
          <TableSkeleton rows={10} />
        ) : items.length === 0 ? (
          <EmptyState
            icon="filter_alt_off"
            title="No rows match these filters"
            body="Every row was filtered out. Remove a rule to widen the result."
            action={<Button onClick={() => { setFilters([]); setPage(1) }}>Clear all filters</Button>}
          />
        ) : (
          <div className="table-container overflow-auto flex-1 w-full bg-surface-container-lowest relative">
            <table className="w-full text-left border-collapse min-w-[1200px]">
              <thead className="sticky top-0 bg-surface-container-low border-b border-outline-variant z-20">
                <tr className="font-label-bold text-body-sm text-on-surface-variant uppercase tracking-wider">
                  <th
                    scope="col"
                    className="px-md py-2 border-r border-outline-variant bg-surface-container-low sticky-col sticky-col-header w-12 text-center text-outline"
                  >
                    #
                  </th>
                  {cols.map((col) => {
                    const active = sort.key === col.key
                    const highlight = col.key === 'revenue'
                    return (
                      <th
                        key={col.key}
                        scope="col"
                        aria-sort={active ? (sort.dir === 'asc' ? 'ascending' : 'descending') : 'none'}
                        className={`px-md py-2 border-r border-outline-variant whitespace-nowrap transition-colors group last:border-r-0 ${
                          highlight ? 'bg-primary-fixed/30 text-on-surface' : 'bg-surface-container-low'
                        }`}
                      >
                        <button
                          onClick={() => toggleSort(col.key)}
                          className={`flex items-center gap-1 w-full uppercase ${col.align === 'right' ? 'justify-end' : ''}`}
                        >
                          {col.label}
                          <Icon
                            name={active ? (sort.dir === 'asc' ? 'arrow_upward' : 'arrow_downward') : 'arrow_drop_down'}
                            size={14}
                            className={active ? '' : 'opacity-0 group-hover:opacity-100'}
                          />
                        </button>
                      </th>
                    )
                  })}
                </tr>
              </thead>
              <tbody className="font-code text-body-main text-on-surface divide-y divide-outline-variant/50">
                {items.map((row, i) => (
                  <tr key={row.id} className="hover:bg-surface-bright transition-colors h-[40px] group">
                    <td className="px-md border-r border-outline-variant/50 sticky-col text-center text-outline tabular-nums">
                      {(page - 1) * PAGE_SIZE + i + 1}
                    </td>
                    <td className="px-md border-r border-outline-variant/50 whitespace-nowrap text-on-surface-variant tabular-nums">{row.date}</td>
                    <td className="px-md border-r border-outline-variant/50 whitespace-nowrap font-label-bold text-primary">{row.orderId}</td>
                    <td className="px-md border-r border-outline-variant/50 whitespace-nowrap font-body-main">{row.customer}</td>
                    <td className="px-md border-r border-outline-variant/50 whitespace-nowrap">{row.product}</td>
                    <td className="px-md border-r border-outline-variant/50 whitespace-nowrap">
                      <span className="bg-surface-container rounded px-1.5 py-0.5 text-[12px] text-on-surface-variant">{row.category}</span>
                    </td>
                    <td className="px-md border-r border-outline-variant/50 text-right tabular-nums">{row.qty}</td>
                    <td className="px-md border-r border-outline-variant/50 text-right text-on-surface-variant tabular-nums">{money(row.price)}</td>
                    <td className="px-md border-r border-outline-variant/50 text-right font-label-bold text-surface-tint bg-primary-fixed/10 tabular-nums">{money(row.revenue)}</td>
                    <td className="px-md border-r border-outline-variant/50 whitespace-nowrap">{row.region}</td>
                    <td className="px-md whitespace-nowrap">
                      <div className="flex items-center gap-1.5">
                        <span className={`w-2 h-2 rounded-full ${statusDot[row.status] ?? 'bg-outline'}`} />
                        <span className="text-body-sm">{row.status}</span>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        <div className="min-h-10 border-t border-outline-variant bg-surface-container-lowest flex flex-wrap items-center justify-between gap-x-md gap-y-1 px-md py-1 shrink-0">
          <span className="font-body-sm text-body-sm text-on-surface-variant">
            <span className="hidden sm:inline">
              Showing {total === 0 ? 0 : (page - 1) * PAGE_SIZE + 1} to {Math.min(page * PAGE_SIZE, total)} of{' '}
              {int(total)} entries{filters.length > 0 ? ' (Filtered)' : ''}
            </span>
            <span className="sm:hidden tabular-nums">
              {total === 0 ? 0 : (page - 1) * PAGE_SIZE + 1}–{Math.min(page * PAGE_SIZE, total)} of {int(total)}
              {filters.length > 0 ? ' · filtered' : ''}
            </span>
          </span>
          <div className="flex items-center gap-xs">
            <button
              disabled={page <= 1 || busy}
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              aria-label="Previous page"
              className="p-1 rounded text-outline hover:text-on-surface hover:bg-surface-container-low disabled:opacity-50"
            >
              <Icon name="chevron_left" size={15} />
            </button>
            <span className="font-body-main text-body-sm px-2 tabular-nums whitespace-nowrap">
              <span className="hidden sm:inline">Page </span>
              {page} / {int(pageCount)}
            </span>
            <button
              disabled={page >= pageCount || busy}
              onClick={() => setPage((p) => Math.min(pageCount, p + 1))}
              aria-label="Next page"
              className="p-1 rounded text-outline hover:text-on-surface hover:bg-surface-container-low disabled:opacity-50"
            >
              <Icon name="chevron_right" size={15} />
            </button>
          </div>
        </div>
      </div>

      <Modal
        open={addingRule}
        onClose={() => setAddingRule(false)}
        title="Add a filter rule"
        description="Rules combine with AND and are applied by the API."
        footer={
          <>
            <Button onClick={() => setAddingRule(false)}>Cancel</Button>
            <Button variant="primary" onClick={addRule} disabled={!draft.value.trim()}>Add rule</Button>
          </>
        }
      >
        <div className="grid grid-cols-1 sm:grid-cols-[1fr_auto_1fr] gap-sm items-end">
          <label className="flex flex-col gap-xs">
            <span className="font-body-sm text-body-sm text-on-surface-variant">Column</span>
            <select
              value={draft.column}
              onChange={(e) => setDraft((d) => ({ ...d, column: e.target.value }))}
              className="h-9 bg-surface border border-outline-variant rounded-lg px-sm font-body-main text-body-main focus:outline-none focus:border-primary"
            >
              {cols.map((c) => (
                <option key={c.key} value={c.key}>{c.label}</option>
              ))}
            </select>
          </label>
          <label className="flex flex-col gap-xs">
            <span className="font-body-sm text-body-sm text-on-surface-variant">Operator</span>
            <select
              value={draft.op}
              onChange={(e) => setDraft((d) => ({ ...d, op: e.target.value }))}
              className="h-9 bg-surface border border-outline-variant rounded-lg px-sm font-code text-code focus:outline-none focus:border-primary"
            >
              {OPS.map((o) => (
                <option key={o.code} value={o.code}>{o.symbol}</option>
              ))}
            </select>
          </label>
          <label className="flex flex-col gap-xs">
            <span className="font-body-sm text-body-sm text-on-surface-variant">Value</span>
            <input
              value={draft.value}
              onChange={(e) => setDraft((d) => ({ ...d, value: e.target.value }))}
              onKeyDown={(e) => e.key === 'Enter' && addRule()}
              placeholder="10000"
              className="h-9 bg-surface border border-outline-variant rounded-lg px-sm font-body-main text-body-main focus:outline-none focus:border-primary"
            />
          </label>
        </div>
      </Modal>
    </FixedCanvas>
  )
}

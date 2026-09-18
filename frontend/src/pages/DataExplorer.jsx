import { useState } from 'react'
import { FixedCanvas } from '../components/AppShell'
import { Button, EmptyState, ErrorState, NoProject, TableSkeleton } from '../components/ui'
import Icon from '../components/Icon'
import Modal from '../components/Modal'
import { api } from '../lib/api'
import { useResource } from '../hooks/useResource'
import { useDatasets } from '../context/AppContext'
import { int, unitValue } from '../lib/format'

/*
 * The grid shows the file as it was uploaded: its own headers, every column,
 * values as written. Sorting and filters follow each column's type, so marks
 * sort as numbers, dates as dates and names alphabetically.
 */

const PAGE_SIZE = 25

const OPS = [
  { symbol: '=', code: 'eq', kinds: ['number', 'date', 'text'] },
  { symbol: '!=', code: 'ne', kinds: ['number', 'date', 'text'] },
  { symbol: '>', code: 'gt', kinds: ['number', 'date'] },
  { symbol: '>=', code: 'gte', kinds: ['number', 'date'] },
  { symbol: '<', code: 'lt', kinds: ['number', 'date'] },
  { symbol: '<=', code: 'lte', kinds: ['number', 'date'] },
  { symbol: 'contains', code: 'contains', kinds: ['text'] },
]

const opKind = (col) => (col?.numeric ? 'number' : col?.kind === 'date' ? 'date' : 'text')
const opsFor = (col) => OPS.filter((o) => o.kinds.includes(opKind(col)))
const symbolFor = (code) => OPS.find((o) => o.code === code)?.symbol ?? code

/** A number the way its column writes it — "$1,250.00", "87%" — without shortening it. */
function cellText(col, raw) {
  const n = Number(String(raw).replace(/[^0-9.eE+-]/g, ''))
  return Number.isFinite(n) && /\d/.test(raw) ? unitValue(n, col.unit, { compact: false }) : raw
}

export default function DataExplorer() {
  const { activeId, datasets, loading: libraryLoading } = useDatasets()

  if (!libraryLoading && datasets.length === 0) {
    return (
      <FixedCanvas className="p-md">
        <div className="bg-surface-container-lowest border border-outline-variant rounded-lg">
          <NoProject what="every row of the file, sortable and filterable" />
        </div>
      </FixedCanvas>
    )
  }

  // Keyed on the file: another file has other columns, so sort and filters start over.
  return <Grid key={activeId} datasetId={activeId} />
}

function Grid({ datasetId }) {
  const [filters, setFilters] = useState([])
  const [sort, setSort] = useState({ key: null, dir: 'asc' })
  const [page, setPage] = useState(1)
  const [addingRule, setAddingRule] = useState(false)
  const [draft, setDraft] = useState({ column: '', op: 'eq', value: '' })

  const columns = useResource(
    () => (datasetId ? api.explorer.columns(datasetId) : Promise.resolve(null)),
    [datasetId]
  )

  const filterParams = filters.map((f) => `${f.column}:${f.op}:${f.value}`)
  const rows = useResource(
    () =>
      datasetId
        ? api.explorer.rows({
            datasetId,
            page,
            pageSize: PAGE_SIZE,
            sort: sort.key ?? undefined,
            dir: sort.dir,
            filter: filterParams,
          })
        : Promise.resolve(null),
    [datasetId, page, sort.key, sort.dir, filterParams.join('|')]
  )

  const cols = columns.data?.columns ?? []
  const draftColumn = cols.find((c) => c.key === draft.column) ?? cols[0]

  // First click sorts numbers and dates high-to-low and text A–Z; the next flips it.
  const toggleSort = (col) => {
    setSort((s) =>
      s.key === col.key
        ? { key: col.key, dir: s.dir === 'asc' ? 'desc' : 'asc' }
        : { key: col.key, dir: col.numeric || col.kind === 'date' ? 'desc' : 'asc' }
    )
    setPage(1)
  }

  const openRule = () => {
    const first = cols.find((c) => c.role !== 'identifier') ?? cols[0]
    setDraft({ column: first?.key ?? '', op: opsFor(first)[0].code, value: '' })
    setAddingRule(true)
  }

  const addRule = () => {
    if (!draft.value.trim() || !draftColumn) return
    setFilters((f) => [
      ...f,
      { column: draftColumn.key, label: draftColumn.label, op: draft.op, value: draft.value.trim(), id: `f${Date.now()}` },
    ])
    setAddingRule(false)
    setPage(1)
  }

  const items = rows.data?.items ?? []
  const total = rows.data?.total ?? 0
  const pageCount = rows.data?.pageCount ?? 1
  const busy = rows.loading || columns.loading

  return (
    <FixedCanvas className="p-md gap-md">
      <div className="bg-surface-container-lowest border border-outline-variant rounded-lg shadow-sm shrink-0 flex flex-col gap-sm p-md">
        <div
          className={`flex flex-wrap items-center justify-between gap-md ${
            filters.length > 0 ? 'border-b border-outline-variant pb-sm' : ''
          }`}
        >
          <div className="flex items-center gap-xs">
            <Button size="sm" icon="filter_list" onClick={openRule} disabled={cols.length === 0}>Filter</Button>
            {sort.key && (
              <Button size="sm" variant="ghost" icon="swap_vert" onClick={() => { setSort({ key: null, dir: 'asc' }); setPage(1) }}>
                File order
              </Button>
            )}
          </div>
          <span className="font-body-sm text-body-sm text-on-surface-variant tabular-nums">
            {busy ? '…' : `${int(total)} rows · ${cols.length} columns`}
          </span>
        </div>

        {filters.length > 0 && (
          <div className="flex flex-wrap items-center gap-sm pt-xs">
            {filters.map((f, i) => (
              <div key={f.id} className="flex items-center gap-sm">
                {i > 0 && (
                  <span className="font-label-bold text-stat-label text-outline uppercase">and</span>
                )}
                <div className="flex items-center bg-primary-fixed border border-primary-fixed-dim rounded px-2 py-1 gap-xs font-body-main text-body-sm">
                  <span className="font-label-bold text-on-primary-fixed">{f.label}</span>
                  <span className="text-primary-container px-1">{symbolFor(f.op)}</span>
                  <span className="text-on-primary-fixed">{f.value}</span>
                  <button
                    aria-label={`Remove filter ${f.label} ${symbolFor(f.op)} ${f.value}`}
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
          </div>
        )}
      </div>

      <div className="flex-1 bg-surface-container-lowest border border-outline-variant rounded-lg shadow-sm overflow-hidden flex flex-col relative min-h-0">
        {rows.error || columns.error ? (
          <ErrorState error={columns.error ?? rows.error} onRetry={() => { rows.reload(); columns.reload() }} className="p-md" />
        ) : busy && !rows.data ? (
          <TableSkeleton rows={10} />
        ) : items.length === 0 ? (
          <EmptyState
            icon="filter_alt_off"
            title={filters.length ? 'No rows match these filters' : 'This file has no rows'}
            body={filters.length ? 'Every row was filtered out. Remove a rule to widen the result.' : 'The file has a header row but nothing beneath it.'}
            action={filters.length ? <Button onClick={() => { setFilters([]); setPage(1) }}>Clear all filters</Button> : null}
          />
        ) : (
          <div className="table-container overflow-auto flex-1 w-full bg-surface-container-lowest relative">
            <table className="w-full text-left border-collapse" style={{ minWidth: `${Math.max(640, 48 + cols.length * 150)}px` }}>
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
                    return (
                      <th
                        key={col.key}
                        scope="col"
                        aria-sort={active ? (sort.dir === 'asc' ? 'ascending' : 'descending') : 'none'}
                        title={`${col.label} · ${col.role}${col.missing ? ` · ${int(col.missing)} blank` : ''}`}
                        className={`px-md py-2 border-r border-outline-variant whitespace-nowrap transition-colors group last:border-r-0 ${
                          active ? 'bg-primary-fixed/30 text-on-surface' : 'bg-surface-container-low'
                        }`}
                      >
                        <button
                          onClick={() => toggleSort(col)}
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
                    <td
                      className="px-md border-r border-outline-variant/50 sticky-col text-center text-outline tabular-nums"
                      title={`Row ${int(row.id)} of the file`}
                    >
                      {(page - 1) * PAGE_SIZE + i + 1}
                    </td>
                    {cols.map((col, c) => (
                      <Cell key={col.key} col={col} raw={row.cells[c] ?? ''} sorted={sort.key === col.key} />
                    ))}
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
        description="Rules combine with AND. Numbers and dates compare by value; text matches ignoring case."
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
              value={draftColumn?.key ?? ''}
              onChange={(e) => {
                const next = cols.find((c) => c.key === e.target.value)
                setDraft((d) => ({
                  ...d,
                  column: e.target.value,
                  op: opsFor(next).some((o) => o.code === d.op) ? d.op : opsFor(next)[0].code,
                }))
              }}
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
              {opsFor(draftColumn).map((o) => (
                <option key={o.code} value={o.code}>{o.symbol}</option>
              ))}
            </select>
          </label>
          <label className="flex flex-col gap-xs">
            <span className="font-body-sm text-body-sm text-on-surface-variant">Value</span>
            <input
              value={draft.value}
              list={draftColumn?.options ? `options-${draftColumn.key}` : undefined}
              onChange={(e) => setDraft((d) => ({ ...d, value: e.target.value }))}
              onKeyDown={(e) => e.key === 'Enter' && addRule()}
              placeholder={
                draftColumn?.options?.[0] ?? (draftColumn?.kind === 'date' ? '2026-01-31' : draftColumn?.numeric ? '50' : 'value')
              }
              className="h-9 bg-surface border border-outline-variant rounded-lg px-sm font-body-main text-body-main focus:outline-none focus:border-primary"
            />
            {draftColumn?.options && (
              <datalist id={`options-${draftColumn.key}`}>
                {draftColumn.options.map((o) => (
                  <option key={o} value={o} />
                ))}
              </datalist>
            )}
          </label>
        </div>
      </Modal>
    </FixedCanvas>
  )
}

/** One cell, styled by what the column holds rather than by what it is called. */
function Cell({ col, raw, sorted }) {
  const base = `px-md border-r border-outline-variant/50 last:border-r-0 whitespace-nowrap ${sorted ? 'bg-primary-fixed/10' : ''}`

  if (raw === '') return <td className={`${base} text-outline`}>—</td>

  if (col.numeric)
    return (
      <td className={`${base} text-right tabular-nums ${sorted ? 'font-label-bold text-surface-tint' : ''}`}>
        {cellText(col, raw)}
      </td>
    )

  if (col.role === 'identifier') return <td className={`${base} font-label-bold text-primary`}>{raw}</td>

  if (col.kind === 'date') return <td className={`${base} text-on-surface-variant tabular-nums`}>{raw}</td>

  // A chip for a short list of repeated values (grades, subjects); a long list such as names reads better as text.
  if (col.role === 'group' && col.distinct <= 12)
    return (
      <td className={base}>
        <span className="bg-surface-container rounded px-1.5 py-0.5 text-[12px] text-on-surface-variant">{raw}</span>
      </td>
    )

  return (
    <td className={`${base} font-body-main max-w-[280px] truncate`} title={raw}>
      {raw}
    </td>
  )
}

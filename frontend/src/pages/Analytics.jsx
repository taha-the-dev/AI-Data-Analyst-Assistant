import { useState } from 'react'
import { PageCanvas } from '../components/AppShell'
import {
  Button, ErrorState, Pagination, Panel, PanelHeader, SegmentedControl, Select, Skeleton, NoProject,
} from '../components/ui'
import Figure from '../components/Figure'
import Icon from '../components/Icon'
import { api } from '../lib/api'
import { useResource } from '../hooks/useResource'
import { useDatasets, usePageActions } from '../context/AppContext'
import { downloadCsv } from '../lib/csv'
import { bucketLabel, int, unitValue } from '../lib/format'

/*
 * Analytics offers the uploaded file's own columns. Its numeric columns are the
 * metrics, its groupings and dates the dimensions, its low-cardinality columns
 * the filters — a mark sheet is explored as marks by subject, an order export
 * as revenue by month. It opens on what the dashboard recognised.
 */

const AGGREGATES = [
  { value: 'avg', label: 'Average' },
  { value: 'sum', label: 'Sum' },
  { value: 'count', label: 'Row count' },
  { value: 'median', label: 'Median' },
  { value: 'min', label: 'Minimum' },
  { value: 'max', label: 'Maximum' },
]

const BUCKETS = [
  { value: 'auto', label: 'Auto' },
  { value: 'day', label: 'Day' },
  { value: 'month', label: 'Month' },
  { value: 'quarter', label: 'Quarter' },
  { value: 'year', label: 'Year' },
]

const CHARTS = [
  { value: 'bar', label: 'Columns', icon: 'bar_chart' },
  { value: 'line', label: 'Trend line', icon: 'show_chart' },
  { value: 'donut', label: 'Share', icon: 'donut_small' },
  { value: 'ranked', label: 'Ranked bars', icon: 'leaderboard' },
]

const ROWS_PER_PAGE = 8

export default function Analytics() {
  const { datasets, activeId, active, loading: libraryLoading } = useDatasets()
  const fields = useResource(
    () => (activeId ? api.analytics.fields(activeId) : Promise.resolve(null)),
    [activeId]
  )

  if (!libraryLoading && datasets.length === 0) {
    return (
      <PageCanvas>
        <Panel>
          <NoProject what="a chart and a table of any column grouped by another" />
        </Panel>
      </PageCanvas>
    )
  }

  if (fields.error) {
    return (
      <PageCanvas>
        <ErrorState error={fields.error} onRetry={fields.reload} />
      </PageCanvas>
    )
  }

  if (fields.loading || !fields.data) {
    return (
      <PageCanvas className="gap-sm">
        <div className="flex gap-sm">
          {Array.from({ length: 3 }).map((_, i) => (
            <Skeleton key={i} className="h-[30px] w-40" />
          ))}
        </div>
        <Skeleton className="h-[380px] w-full" />
      </PageCanvas>
    )
  }

  if (fields.data.dimensions.length === 0) {
    return (
      <PageCanvas>
        <Panel className="p-lg">
          <p className="font-label-bold text-label-bold text-on-surface">Nothing to group by</p>
          <p className="font-body-main text-body-main text-on-surface-variant mt-xs">
            {active?.name ?? 'This file'} has no column with repeated values or dates, so there is nothing to break a
            figure down by. The Data Explorer still shows every row.
          </p>
        </Panel>
      </PageCanvas>
    )
  }

  // Keyed on the file, so switching projects starts from that file's defaults.
  return <Workspace key={activeId} fields={fields.data} datasetId={activeId} datasetName={active?.name} />
}

function Workspace({ fields, datasetId, datasetName }) {
  const [metric, setMetric] = useState(fields.defaultMetric ?? '')
  const [dimension, setDimension] = useState(fields.defaultDimension ?? fields.dimensions[0].key)
  const [aggregate, setAggregate] = useState(fields.defaultMetric ? fields.defaultAggregate : 'count')
  const [bucket, setBucket] = useState('auto')
  const [chartChoice, setChartChoice] = useState(null)
  const [filters, setFilters] = useState([])
  const [filtersOpen, setFiltersOpen] = useState(false)
  const [page, setPage] = useState(1)

  const dimensionField = fields.dimensions.find((d) => d.key === dimension)
  const isDate = dimensionField?.kind === 'date'
  const counting = aggregate === 'count' || !metric
  const chart = chartChoice ?? (isDate ? 'line' : 'bar')

  const filterParams = filters.map((f) => `${f.column}:eq:${f.value}`)
  const result = useResource(
    () =>
      api.analytics.run({
        datasetId,
        metric: counting ? undefined : metric,
        aggregate: counting ? 'count' : aggregate,
        dimension,
        bucket: isDate && bucket !== 'auto' ? bucket : undefined,
        filter: filterParams,
        limit: isDate ? 60 : 50,
      }),
    [datasetId, metric, aggregate, dimension, bucket, filterParams.join('|')]
  )

  const data = result.data
  const figures = data?.figures ?? []
  const formatFull = (v) => unitValue(v, data?.unit, { compact: false })
  const total = data?.total ?? null

  usePageActions(
    () => ({
      filterLabel: 'Filter this view',
      onFilter: () => setFiltersOpen((v) => !v),
      exportLabel: 'Export these figures as CSV',
      onExport: figures.length
        ? () =>
            downloadCsv(
              `${datasetName ?? 'dataset'}-${data.title.replace(/\s+/g, '-').toLowerCase()}`,
              [
                { label: data.dimensionLabel, value: (r) => bucketLabel(r.label) },
                { label: data.title, value: (r) => r.value },
              ],
              figures
            )
        : undefined,
    }),
    [figures.length, data?.title, datasetName]
  )

  const pageCount = Math.max(1, Math.ceil(figures.length / ROWS_PER_PAGE))
  const shown = figures.slice((page - 1) * ROWS_PER_PAGE, page * ROWS_PER_PAGE)
  const reset = (fn) => (value) => {
    fn(value)
    setPage(1)
  }

  const change = (index) => {
    const previous = figures[index - 1]
    if (!previous || !previous.value) return null
    return ((figures[index].value - previous.value) / Math.abs(previous.value)) * 100
  }

  return (
    <PageCanvas className="gap-sm">
      <div className="flex flex-wrap items-center gap-sm">
        <Select
          label="Aggregate"
          value={counting ? 'count' : aggregate}
          onChange={reset((v) => {
            setAggregate(v)
            if (v !== 'count' && !metric && fields.metrics[0]) setMetric(fields.metrics[0].key)
          })}
          options={fields.metrics.length ? AGGREGATES : AGGREGATES.filter((a) => a.value === 'count')}
        />
        {!counting && (
          <Select
            label="Metric"
            value={metric}
            onChange={reset(setMetric)}
            options={fields.metrics.map((m) => ({ value: m.key, label: m.label }))}
          />
        )}
        <Select
          label="By"
          value={dimension}
          onChange={reset((v) => {
            setDimension(v)
            setChartChoice(null)
          })}
          options={fields.dimensions.map((d) => ({ value: d.key, label: d.label }))}
        />
        {isDate && <Select label="Per" value={bucket} onChange={reset(setBucket)} options={BUCKETS} />}

        <div className="flex items-center gap-md ml-auto">
          <SegmentedControl label="Chart type" value={chart} onChange={setChartChoice} options={CHARTS} />
        </div>
      </div>

      {(filtersOpen || filters.length > 0) && (
        <FilterBar
          fields={fields.filters}
          filters={filters}
          onChange={(next) => {
            setFilters(next)
            setPage(1)
          }}
        />
      )}

      {data?.summary && <SummaryStrip summary={data.summary} unit={fields.metrics.find((m) => m.key === metric)?.unit} />}

      <Panel className="p-md flex flex-col">
        <div className="flex flex-wrap items-start justify-between gap-md">
          <div>
            <h3 className="font-section-title text-[15px] leading-5 text-on-surface">{data?.title ?? '…'}</h3>
            <p className="font-body-main text-body-main text-on-surface-variant mt-xs">
              {data
                ? `${int(data.rowsMatched)} of ${int(data.rowsScanned)} rows · ${int(data.groupCount)} ${
                    data.bucket ? `${data.bucket}s` : 'groups'
                  }${data.groupCount > figures.length ? `, ${figures.length} shown` : ''} · computed in ${data.durationMs} ms`
                : 'Running the query…'}
            </p>
          </div>
          {data?.metricLabel && (
            <span className="inline-flex items-center gap-sm font-body-sm text-body-sm text-on-surface-variant">
              <span className="w-3 h-3 rounded-sm bg-primary-container" />
              {data.metricLabel}
            </span>
          )}
        </div>

        <div className="mt-lg min-h-[130px] flex">
          {result.error ? (
            <ErrorState error={result.error} onRetry={result.reload} className="w-full" />
          ) : result.loading && !data ? (
            <Skeleton className="h-[210px] w-full" />
          ) : figures.length === 0 ? (
            <p className="font-body-main text-body-main text-on-surface-variant">
              Nothing matched this combination. Remove a filter or pick another column.
            </p>
          ) : (
            <Figure
              kind={{ bar: 'columns', line: 'line', donut: 'donut', ranked: 'bars' }[chart]}
              figures={chart === 'bar' ? figures.slice(0, 40) : chart === 'donut' ? figures.slice(0, 4) : chart === 'ranked' ? figures.slice(0, 12) : figures}
              unit={data.unit}
              height={chart === 'bar' ? 300 : 260}
              id="analytics"
              ariaLabel={data.title}
              fit={false}
            />
          )}
        </div>
      </Panel>

      <Panel className="flex flex-col overflow-hidden">
        <div className="p-md">
          <PanelHeader
            title="Tabular Data"
            action={
              <span className="font-body-sm text-body-sm text-on-surface-variant">
                {figures.length} {figures.length === 1 ? 'entry' : 'entries'}
              </span>
            }
          />
        </div>

        {result.loading && !data ? (
          <div className="px-lg pb-lg space-y-sm">
            {Array.from({ length: ROWS_PER_PAGE }).map((_, i) => (
              <Skeleton key={i} className="h-9 w-full" />
            ))}
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[560px] text-left border-collapse">
              <thead>
                <tr className="border-y border-outline-variant bg-surface-container-low/60">
                  <th scope="col" className="font-label-bold text-stat-label uppercase text-on-surface-variant py-sm px-md">
                    {data?.bucket ?? data?.dimensionLabel}
                  </th>
                  <th scope="col" className="font-label-bold text-stat-label uppercase text-on-surface-variant py-sm px-md text-right">
                    {data?.title}
                  </th>
                  {total !== null && (
                    <th scope="col" className="font-label-bold text-stat-label uppercase text-on-surface-variant py-sm px-md text-right">
                      Share of total
                    </th>
                  )}
                  {isDate && (
                    <th scope="col" className="font-label-bold text-stat-label uppercase text-on-surface-variant py-sm px-md text-right">
                      vs previous period
                    </th>
                  )}
                </tr>
              </thead>
              <tbody className="divide-y divide-outline-variant/50">
                {shown.map((f, i) => {
                  const delta = isDate ? change((page - 1) * ROWS_PER_PAGE + i) : null
                  return (
                    <tr
                      key={f.label}
                      className="hover:bg-surface-container-low/60 transition-colors animate-fade-up"
                      style={{ animationDelay: `${i * 40}ms` }}
                    >
                      <td className="py-sm px-md font-body-main text-body-main text-on-surface">{bucketLabel(f.label)}</td>
                      <td className="py-sm px-md text-right font-code text-code text-on-surface tabular-nums">
                        {formatFull(f.value)}
                      </td>
                      {total !== null && (
                        <td className="py-sm px-md text-right font-code text-code text-on-surface-variant tabular-nums">
                          {total ? `${((f.value / total) * 100).toFixed(1)}%` : '—'}
                        </td>
                      )}
                      {isDate && (
                        <td className="py-sm px-md text-right">
                          {delta === null ? (
                            <span className="font-body-sm text-body-sm text-on-surface-variant">—</span>
                          ) : (
                            <span
                              className={`inline-flex items-center rounded-md px-1.5 py-[2px] font-label-bold text-[13px] tabular-nums ${
                                delta >= 0 ? 'bg-success-container text-success' : 'bg-danger-container text-danger'
                              }`}
                            >
                              {delta >= 0 ? '+' : ''}
                              {delta.toFixed(1)}%
                            </span>
                          )}
                        </td>
                      )}
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}

        <div className="border-t border-outline-variant px-lg py-md">
          <Pagination
            page={page}
            pageCount={pageCount}
            onPage={(p) => setPage(Math.min(pageCount, Math.max(1, p)))}
            summary={
              figures.length === 0
                ? 'No entries'
                : `Showing ${(page - 1) * ROWS_PER_PAGE + 1} to ${Math.min(page * ROWS_PER_PAGE, figures.length)} of ${
                    figures.length
                  } entries`
            }
          />
        </div>
      </Panel>
    </PageCanvas>
  )
}

/** Count, average, median and range of the chosen metric across the rows that passed the filters. */
function SummaryStrip({ summary, unit }) {
  const avgUnit = unit ? { ...unit, decimals: Math.max(unit.decimals, unit.prefix ? 2 : 1) } : undefined
  const stats = [
    ['Values', int(summary.count)],
    ['Average', unitValue(summary.mean, avgUnit)],
    ['Median', unitValue(summary.median, avgUnit)],
    ['Lowest', unitValue(summary.min, unit)],
    ['Highest', unitValue(summary.max, unit)],
    ['Sum', unitValue(summary.sum, unit)],
  ]
  return (
    <div className="grid grid-cols-3 md:grid-cols-6 gap-sm">
      {stats.map(([label, value]) => (
        <div key={label} className="bg-surface-container-lowest border border-outline-variant rounded-lg px-sm py-xs min-w-0">
          <p className="font-label-bold text-stat-label uppercase text-on-surface-variant">{label}</p>
          <p className="font-code text-code text-on-surface tabular-nums truncate" title={value}>{value}</p>
        </div>
      ))}
    </div>
  )
}

/** Narrow the rows by any grouping column's values before anything is aggregated. */
function FilterBar({ fields, filters, onChange }) {
  const [column, setColumn] = useState(fields[0]?.key ?? '')
  const field = fields.find((f) => f.key === column)
  const [value, setValue] = useState('')
  const options = field?.options ?? []

  if (fields.length === 0) {
    return (
      <Panel className="p-md font-body-main text-body-main text-on-surface-variant animate-fade-up">
        This file has no column with few enough distinct values to filter by. Use the Data Explorer for rules on any column.
      </Panel>
    )
  }

  const add = () => {
    const chosen = value || options[0]
    if (!chosen) return
    onChange([...filters.filter((f) => !(f.column === column && f.value === chosen)), { column, label: field.label, value: chosen }])
    setValue('')
  }

  return (
    <Panel className="p-md flex flex-col gap-sm animate-fade-up">
      <div className="flex flex-wrap items-center gap-sm">
        <Select
          label="Where"
          value={column}
          onChange={(v) => {
            setColumn(v)
            setValue('')
          }}
          options={fields.map((f) => ({ value: f.key, label: f.label }))}
        />
        <span className="font-code text-code text-on-surface-variant">=</span>
        <Select
          value={value || options[0] || ''}
          onChange={setValue}
          options={options.map((o) => ({ value: o, label: o }))}
        />
        <Button size="sm" icon="add" onClick={add}>Add filter</Button>
        {filters.length > 0 && (
          <button
            onClick={() => onChange([])}
            className="ml-auto font-label-bold text-label-bold text-primary hover:text-surface-tint transition-colors"
          >
            Clear filters
          </button>
        )}
      </div>
      {filters.length > 0 && (
        <div className="flex flex-wrap items-center gap-sm">
          {filters.map((f, i) => (
            <span key={`${f.column}-${f.value}`} className="flex items-center gap-sm">
              {i > 0 && <span className="font-label-bold text-stat-label text-outline uppercase">and</span>}
              <span className="flex items-center bg-primary-fixed border border-primary-fixed-dim rounded px-2 py-1 gap-xs font-body-sm text-body-sm">
                <span className="font-label-bold text-on-primary-fixed">{f.label}</span>
                <span className="text-primary-container">=</span>
                <span className="text-on-primary-fixed">{f.value}</span>
                <button
                  aria-label={`Remove filter ${f.label} = ${f.value}`}
                  onClick={() => onChange(filters.filter((x) => x !== f))}
                  className="text-primary hover:text-on-primary-fixed ml-1"
                >
                  <Icon name="close" size={14} />
                </button>
              </span>
            </span>
          ))}
        </div>
      )}
    </Panel>
  )
}

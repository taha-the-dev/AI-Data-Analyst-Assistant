import { useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageCanvas } from '../components/AppShell'
import {
  ErrorState, Pagination, Panel, PanelHeader, SegmentedControl, Select, Skeleton, NoProject,
} from '../components/ui'
import { ColumnChart, DonutChart, TrackBars, TrendChart } from '../components/charts'
import Icon from '../components/Icon'
import { api } from '../lib/api'
import { useResource } from '../hooks/useResource'
import { useDatasets, usePageActions } from '../context/AppContext'
import { downloadCsv } from '../lib/csv'
import { bucketLabel, compactMoney, int, money } from '../lib/format'

const METRICS = [
  { value: 'revenue', label: 'Revenue' },
  { value: 'qty', label: 'Quantity' },
  { value: 'price', label: 'Unit price' },
]

const DIMENSIONS = [
  { value: 'month', label: 'Month' },
  { value: 'quarter', label: 'Quarter' },
  { value: 'category', label: 'Category' },
  { value: 'region', label: 'Region' },
  { value: 'product', label: 'Product' },
  { value: 'customer', label: 'Customer' },
  { value: 'status', label: 'Status' },
]

const AGGREGATES = [
  { value: 'sum', label: 'Sum' },
  { value: 'avg', label: 'Average' },
  { value: 'count', label: 'Row count' },
  { value: 'max', label: 'Maximum' },
]

const CHARTS = [
  { value: 'bar', label: 'Columns', icon: 'bar_chart' },
  { value: 'line', label: 'Trend line', icon: 'show_chart' },
  { value: 'donut', label: 'Share', icon: 'donut_small' },
  { value: 'ranked', label: 'Ranked bars', icon: 'leaderboard' },
]

const ROWS_PER_PAGE = 6
const TIME_BUCKETS = { month: 'month', quarter: 'quarter' }

/** The spec the toolbar describes — the exact object posted to /api/query/run. */
function buildSpec({ metric, dimension, aggregate, region, chart }) {
  const bucket = TIME_BUCKETS[dimension]
  const filters = region === 'all' ? [] : [{ column: 'region', op: '=', value: region }]
  const metricLabel = METRICS.find((m) => m.value === metric)?.label ?? metric
  const dimensionLabel = DIMENSIONS.find((d) => d.value === dimension)?.label ?? dimension

  return {
    intent: bucket ? 'trend' : 'aggregate',
    groupBy: bucket ? 'date' : dimension,
    timeBucket: bucket ?? null,
    metric,
    aggregate,
    filters,
    sort: bucket ? 'label asc' : 'value desc',
    limit: bucket ? 24 : 12,
    chart,
    title: `${metricLabel} by ${dimensionLabel.toLowerCase()}`,
  }
}

export default function Analytics() {
  const { datasets, activeId, active, loading: libraryLoading } = useDatasets()
  const navigate = useNavigate()

  const [metric, setMetric] = useState('revenue')
  const [dimension, setDimension] = useState('month')
  const [aggregate, setAggregate] = useState('sum')
  const [region, setRegion] = useState('all')
  const [chart, setChart] = useState('bar')
  const [filtersOpen, setFiltersOpen] = useState(false)
  const [page, setPage] = useState(1)

  const spec = useMemo(
    () => buildSpec({ metric, dimension, aggregate, region, chart }),
    [metric, dimension, aggregate, region, chart]
  )

  const result = useResource(
    () => (activeId ? api.runSpec(spec, activeId) : Promise.resolve(null)),
    [activeId, spec]
  )

  // The region list is itself a query, so the filter can only offer values the
  // file actually contains.
  const regions = useResource(
    () =>
      activeId
        ? api.runSpec(
            { intent: 'aggregate', groupBy: 'region', metric: 'revenue', aggregate: 'sum', limit: 25 },
            activeId
          )
        : Promise.resolve(null),
    [activeId]
  )

  const figures = result.data?.figures ?? []
  const total = result.data?.total ?? 0
  const isCount = aggregate === 'count'
  const format = isCount || metric === 'qty' ? int : compactMoney
  const formatFull = isCount || metric === 'qty' ? int : (v) => money(v, 2)

  usePageActions(
    () => ({
      filterLabel: 'Filter this view',
      onFilter: () => setFiltersOpen((v) => !v),
      exportLabel: 'Export these figures as CSV',
      onExport: figures.length
        ? () =>
            downloadCsv(
              `${active?.name ?? 'dataset'}-${spec.title.replace(/\s+/g, '-').toLowerCase()}`,
              [
                { label: DIMENSIONS.find((d) => d.value === dimension)?.label ?? 'Group', value: (r) => bucketLabel(r.label) },
                { label: spec.title, value: (r) => r.value },
              ],
              figures
            )
        : undefined,
    }),
    [figures.length, spec.title, dimension, active?.name]
  )

  const pageCount = Math.max(1, Math.ceil(figures.length / ROWS_PER_PAGE))
  const shown = figures.slice((page - 1) * ROWS_PER_PAGE, page * ROWS_PER_PAGE)

  const change = (index) => {
    const previous = figures[index - 1]
    if (!previous || !previous.value) return null
    return ((figures[index].value - previous.value) / Math.abs(previous.value)) * 100
  }

  const askAssistant = () =>
    navigate('/analyst', {
      state: {
        question:
          region === 'all'
            ? `${spec.title} — what stands out?`
            : `${spec.title} in region ${region} — what stands out?`,
      },
    })

  if (!libraryLoading && datasets.length === 0) {
    return (
      <PageCanvas>
        <Panel>
          <NoProject what="a chart and a table of whatever you group by" />
        </Panel>
      </PageCanvas>
    )
  }

  return (
    <PageCanvas className="gap-sm">
      {/* Toolbar — the row of selectors above the chart in the design. */}
      <div className="flex flex-wrap items-center gap-sm">
        <Select label="Metric" value={metric} onChange={setMetric} options={METRICS} />
        <Select label="Dimension" value={dimension} onChange={(v) => { setDimension(v); setPage(1) }} options={DIMENSIONS} />
        <Select label="Aggregate" value={aggregate} onChange={setAggregate} options={AGGREGATES} />

        <div className="flex items-center gap-md ml-auto">
          <SegmentedControl label="Chart type" value={chart} onChange={setChart} options={CHARTS} />
          <button
            onClick={askAssistant}
            className="inline-flex items-center gap-sm h-[30px] px-sm rounded-lg border border-primary-fixed-dim bg-primary-fixed/40 font-label-bold text-label-bold text-primary hover:bg-primary-fixed transition-colors"
          >
            <Icon name="auto_awesome" size={15} />
            AI Insights
          </button>
        </div>
      </div>

      {filtersOpen && (
        <Panel className="p-md flex flex-wrap items-center gap-md animate-fade-up">
          <Select
            label="Region"
            value={region}
            onChange={(v) => { setRegion(v); setPage(1) }}
            options={[
              { value: 'all', label: 'All regions' },
              ...(regions.data?.figures ?? []).map((f) => ({ value: f.label, label: f.label })),
            ]}
          />
          <p className="font-body-sm text-body-sm text-on-surface-variant">
            Filters are applied by the API before anything is grouped.
          </p>
          <button
            onClick={() => { setRegion('all'); setPage(1) }}
            disabled={region === 'all'}
            className="ml-auto font-label-bold text-label-bold text-primary hover:text-surface-tint transition-colors disabled:opacity-40"
          >
            Clear filter
          </button>
        </Panel>
      )}

      <Panel className="p-md flex flex-col">
        <div className="flex flex-wrap items-start justify-between gap-md">
          <div>
            <h3 className="font-section-title text-[15px] leading-5 text-on-surface">{spec.title}</h3>
            <p className="font-body-main text-body-main text-on-surface-variant mt-xs">
              {result.data
                ? `${int(result.data.rowsMatched)} of ${int(result.data.rowsScanned)} rows${
                    region === 'all' ? '' : ` · region ${region}`
                  } · computed in ${result.data.durationMs} ms`
                : 'Running the query…'}
            </p>
          </div>
          <span className="inline-flex items-center gap-sm font-body-sm text-body-sm text-on-surface-variant">
            <span className="w-3 h-3 rounded-sm bg-primary-container" />
            {METRICS.find((m) => m.value === metric)?.label}
          </span>
        </div>

        <div className="mt-lg min-h-[130px] flex">
          {result.error ? (
            <ErrorState error={result.error} onRetry={result.reload} className="w-full" />
          ) : result.loading ? (
            <Skeleton className="h-[210px] w-full" />
          ) : figures.length === 0 ? (
            <p className="font-body-main text-body-main text-on-surface-variant">
              Nothing matched this combination. Clear the filter or pick another dimension.
            </p>
          ) : chart === 'line' ? (
            <TrendChart
              points={figures}
              format={format}
              className="flex-1 h-[210px]"
              gradientId="analyticsTrend"
              ariaLabel={spec.title}
            />
          ) : chart === 'donut' ? (
            <div className="flex-1 flex flex-col items-center justify-center gap-lg">
              <DonutChart
                segments={figures.slice(0, 6)}
                centerLabel={bucketLabel(figures[0].label)}
                centerValue={format(figures[0].value)}
                size="w-48 h-48"
                format={format}
              />
              <ul className="flex flex-wrap justify-center gap-md">
                {figures.slice(0, 6).map((f) => (
                  <li key={f.label} className="font-body-sm text-body-sm text-on-surface-variant">
                    {bucketLabel(f.label)}{' '}
                    <span className="font-code text-on-surface tabular-nums">
                      {total ? `${((f.value / total) * 100).toFixed(1)}%` : '—'}
                    </span>
                  </li>
                ))}
              </ul>
            </div>
          ) : chart === 'ranked' ? (
            <div className="flex-1">
              <TrackBars items={figures.slice(0, 10)} format={format} />
            </div>
          ) : (
            <div className="flex-1 min-w-0">
              <ColumnChart points={figures} format={format} height={320} />
            </div>
          )}
        </div>
      </Panel>

      <Panel className="flex flex-col overflow-hidden">
        <div className="p-md pb-md">
          <PanelHeader
            title="Tabular Data"
            action={
              <span className="font-body-sm text-body-sm text-on-surface-variant">
                {figures.length} {figures.length === 1 ? 'entry' : 'entries'}
              </span>
            }
          />
        </div>

        {result.loading ? (
          <div className="px-lg pb-lg space-y-sm">
            {Array.from({ length: ROWS_PER_PAGE }).map((_, i) => (
              <Skeleton key={i} className="h-9 w-full" />
            ))}
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[640px] text-left border-collapse">
              <thead>
                <tr className="border-y border-outline-variant bg-surface-container-low/60">
                  <th scope="col" className="font-label-bold text-stat-label uppercase text-on-surface-variant py-xs px-md">
                    {DIMENSIONS.find((d) => d.value === dimension)?.label}
                  </th>
                  <th scope="col" className="font-label-bold text-stat-label uppercase text-on-surface-variant py-sm px-md text-right">
                    {spec.title}
                  </th>
                  <th scope="col" className="font-label-bold text-stat-label uppercase text-on-surface-variant py-sm px-md text-right">
                    Share of total
                  </th>
                  <th scope="col" className="font-label-bold text-stat-label uppercase text-on-surface-variant py-xs px-md text-right">
                    vs previous row
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y divide-outline-variant/50">
                {shown.map((f, i) => {
                  const index = (page - 1) * ROWS_PER_PAGE + i
                  const delta = change(index)
                  return (
                    <tr
                      key={f.label}
                      className="hover:bg-surface-container-low/60 transition-colors animate-fade-up"
                      style={{ animationDelay: `${i * 40}ms` }}
                    >
                      <td className="py-sm px-md font-body-main text-body-main text-on-surface">{bucketLabel(f.label)}</td>
                      <td className="py-sm px-sm text-right font-code text-code text-on-surface tabular-nums">
                        {formatFull(f.value)}
                      </td>
                      <td className="py-sm px-sm text-right font-code text-code text-on-surface-variant tabular-nums">
                        {total ? `${((f.value / total) * 100).toFixed(1)}%` : '—'}
                      </td>
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
                : `Showing ${(page - 1) * ROWS_PER_PAGE + 1} to ${Math.min(
                    page * ROWS_PER_PAGE,
                    figures.length
                  )} of ${figures.length} entries`
            }
          />
        </div>
      </Panel>
    </PageCanvas>
  )
}

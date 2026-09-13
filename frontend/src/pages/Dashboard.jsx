import { Link } from 'react-router-dom'
import { PageCanvas, PageHeader } from '../components/AppShell'
import {
  PanelSkeleton, ErrorState, IconTile, Panel, PanelHeader, Skeleton, StatCard, NoProject,
} from '../components/ui'
import { TrackBars, TrendChart } from '../components/charts'
import Icon from '../components/Icon'
import ProjectPicker from '../components/ProjectPicker'
import { api } from '../lib/api'
import { useResource } from '../hooks/useResource'
import { useDatasets, usePageActions } from '../context/AppContext'
import { downloadCsv } from '../lib/csv'
import { bucketLabel, compactMoney, int, money } from '../lib/format'

/* Two extra series the dashboard needs that no endpoint returns ready-made.
   Both are ordinary QuerySpecs, run by the same engine every other figure on
   the screen comes from. */
const TOP_PRODUCTS_SPEC = {
  intent: 'aggregate',
  groupBy: 'product',
  metric: 'revenue',
  aggregate: 'sum',
  sort: 'value desc',
  limit: 5,
  chart: 'bar',
  title: 'Top products by revenue',
}

const ORDER_VOLUME_SPEC = {
  intent: 'trend',
  groupBy: 'date',
  timeBucket: 'month',
  aggregate: 'count',
  sort: 'label asc',
  limit: 24,
  chart: 'line',
  title: 'Orders by month',
}

const insightTones = {
  positive: { wrap: 'border-success/30 bg-success-container/40', icon: 'text-success', glyph: 'check_circle' },
  alert: { wrap: 'border-danger/30 bg-danger-container/40', icon: 'text-danger', glyph: 'warning' },
  neutral: { wrap: 'border-outline-variant bg-surface-container-low', icon: 'text-primary', glyph: 'lightbulb' },
}

export default function Dashboard() {
  const { datasets, activeId, active, revalidate, loading: libraryLoading } = useDatasets()

  const dashboard = useResource(
    () => (activeId ? api.dashboard(activeId) : Promise.resolve(null)),
    [activeId]
  )
  const analytics = useResource(
    () => (activeId ? api.analytics(activeId) : Promise.resolve(null)),
    [activeId]
  )
  const products = useResource(
    () => (activeId ? api.runSpec(TOP_PRODUCTS_SPEC, activeId) : Promise.resolve(null)),
    [activeId]
  )

  const orders = useResource(
    () => (activeId ? api.runSpec(ORDER_VOLUME_SPEC, activeId) : Promise.resolve(null)),
    [activeId]
  )

  const trend = dashboard.data?.revenueTrend ?? []

  usePageActions(
    () =>
      trend.length
        ? {
            exportLabel: 'Export revenue by month as CSV',
            onExport: () =>
              downloadCsv(
                `${active?.name ?? 'dataset'}-revenue-by-month`,
                [
                  { label: 'Month', value: (r) => bucketLabel(r.label) },
                  { label: 'Revenue', value: (r) => r.value },
                ],
                trend
              ),
          }
        : {},
    [trend.length, activeId, active?.name]
  )

  const header = (
    <PageHeader
      title="Dashboard"
      description={
        active
          ? `${int(active.rows)} rows across ${active.columns} columns, computed on read.`
          : 'Choose a project to analyse.'
      }
      aside={<ProjectPicker />}
    />
  )

  // Nothing uploaded yet is not an error; it is the starting point.
  if (!libraryLoading && datasets.length === 0) {
    return (
      <PageCanvas>
        {header}
        <Panel>
          <NoProject what="KPIs, trends and insights" />
        </Panel>
      </PageCanvas>
    )
  }

  const error = dashboard.error ?? analytics.error
  if (error) {
    return (
      <PageCanvas>
        {header}
        <ErrorState
          error={error}
          onRetry={async () => {
            // A project can disappear while this screen is open; re-reading the
            // library first drops a selection the server no longer knows about.
            await revalidate()
            dashboard.reload()
            analytics.reload()
          }}
        />
      </PageCanvas>
    )
  }

  const loading = dashboard.loading || analytics.loading || !dashboard.data || !analytics.data

  if (loading) {
    return (
      <PageCanvas>
        {header}
        <div className="grid grid-cols-2 md:grid-cols-3 xl:grid-cols-6 gap-md">
          {Array.from({ length: 6 }).map((_, i) => (
            <Panel key={i} className="p-md">
              <Skeleton className="h-3 w-20" />
              <Skeleton className="mt-md h-7 w-24" />
            </Panel>
          ))}
        </div>
        <div className="grid grid-cols-12 gap-sm">
          <PanelSkeleton className="col-span-12 lg:col-span-8 h-[340px]" />
          <PanelSkeleton className="col-span-12 lg:col-span-4 h-[340px]" />
        </div>
      </PageCanvas>
    )
  }

  const { categoryBars, insights } = dashboard.data
  // Six tiles, as in the design: the four money figures, then the two that
  // describe the file itself.
  const stats = [
    ...analytics.data.kpis,
    ...dashboard.data.kpis.filter((k) => k.label === 'Total Rows' || k.label === 'Quality Score'),
  ]

  const productTotal = products.data?.total ?? 0
  const orderPoints = orders.data?.figures ?? []
  const totalOrders = orderPoints.reduce((sum, p) => sum + p.value, 0)

  return (
    <PageCanvas>
      {header}

      <div className="grid grid-cols-2 md:grid-cols-3 xl:grid-cols-6 gap-sm stagger">
        {stats.map((k) => (
          <StatCard
            key={k.label}
            label={k.label}
            value={k.value}
            icon={k.icon}
            iconTone={k.iconTone}
            delta={k.delta}
            deltaTone={k.deltaTone}
          />
        ))}
      </div>

      <div className="grid grid-cols-12 gap-sm">
        <Panel className="col-span-12 lg:col-span-8 p-md flex flex-col min-h-[130px]">
          <PanelHeader
            title="Revenue Performance"
            action={
              <span className="font-body-sm text-body-sm text-on-surface-variant">
                {trend.length} months
              </span>
            }
          />
          <div className="flex-1 mt-md min-h-[150px] flex">
            <TrendChart
              points={trend}
              format={compactMoney}
              className="flex-1"
              gradientId="dashboardTrend"
              ariaLabel="Revenue by month"
            />
          </div>
        </Panel>

        <Panel className="col-span-12 lg:col-span-4 p-md flex flex-col">
          <PanelHeader title="Sales by Category" />
          <div className="flex-1 flex items-center mt-md">
            <div className="w-full">
              <TrackBars items={categoryBars} format={compactMoney} />
            </div>
          </div>
        </Panel>
      </div>

      <div className="grid grid-cols-12 gap-sm">
        <Panel className="col-span-12 xl:col-span-5 p-md flex flex-col">
          <PanelHeader
            title="Top Products"
            action={
              <Link
                to="/explorer"
                className="font-label-bold text-label-bold text-primary hover:text-surface-tint transition-colors"
              >
                View All
              </Link>
            }
          />

          {products.error ? (
            <ErrorState error={products.error} onRetry={products.reload} className="mt-md" />
          ) : products.loading ? (
            <div className="mt-lg space-y-sm">
              {Array.from({ length: 5 }).map((_, i) => (
                <Skeleton key={i} className="h-8 w-full" />
              ))}
            </div>
          ) : (
            <table className="w-full mt-md text-left border-collapse">
              <thead>
                <tr className="bg-surface-container-low">
                  <th scope="col" className="font-label-bold text-stat-label uppercase text-on-surface-variant py-sm px-md rounded-l-lg">
                    Product
                  </th>
                  <th scope="col" className="font-label-bold text-stat-label uppercase text-on-surface-variant py-sm px-md text-right">
                    Revenue
                  </th>
                  <th scope="col" className="font-label-bold text-stat-label uppercase text-on-surface-variant py-sm px-md text-right rounded-r-lg">
                    Share
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y divide-outline-variant/50">
                {(products.data?.figures ?? []).map((p, i) => (
                  <tr
                    key={p.label}
                    className="hover:bg-surface-container-low/60 transition-colors animate-fade-up"
                    style={{ animationDelay: `${i * 45}ms` }}
                  >
                    <td className="py-sm px-sm font-body-main text-body-main text-on-surface">{bucketLabel(p.label)}</td>
                    <td className="py-sm px-sm text-right font-code text-code text-on-surface tabular-nums">
                      {money(p.value, 0)}
                    </td>
                    <td className="py-sm px-sm text-right font-code text-code text-success tabular-nums">
                      {productTotal ? `${((p.value / productTotal) * 100).toFixed(1)}%` : '—'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </Panel>

        <Panel className="col-span-12 md:col-span-6 xl:col-span-3 p-md flex flex-col">
          <PanelHeader title="Order Volume" />
          <p className="mt-md flex items-baseline gap-sm">
            <span className="font-kpi-value text-stat-value text-on-surface tabular-nums">
              {int(totalOrders)}
            </span>
            <span className="font-body-main text-body-main text-on-surface-variant">orders</span>
          </p>
          <div className="flex-1 min-h-[110px] mt-md flex">
            {orders.loading ? (
              <Skeleton className="h-full w-full" />
            ) : orderPoints.length ? (
              <TrendChart
                points={orderPoints}
                format={int}
                showAxis={false}
                className="flex-1"
                gradientId="dashboardOrders"
                ariaLabel="Orders by month"
              />
            ) : (
              <p className="font-body-sm text-body-sm text-on-surface-variant">No dated rows to plot.</p>
            )}
          </div>
        </Panel>

        <Panel className="col-span-12 md:col-span-6 xl:col-span-4 p-md flex flex-col">
          <PanelHeader title="AI Insights" icon="auto_awesome" />
          <ul className="mt-md flex flex-col gap-xs">
            {insights.map((insight, index) => {
              const tone = insightTones[insight.tone] ?? insightTones.neutral
              return (
                <li
                  key={insight.title}
                  className={`border rounded-lg p-md flex gap-sm animate-fade-up ${tone.wrap}`}
                  style={{ animationDelay: `${index * 70}ms` }}
                >
                  <Icon name={insight.icon || tone.glyph} size={18} className={`${tone.icon} shrink-0 mt-[2px]`} />
                  <p className="font-body-main text-body-main text-on-surface">
                    <span className="font-label-bold">{insight.title}. </span>
                    {insight.body}
                  </p>
                </li>
              )
            })}
          </ul>
        </Panel>
      </div>

      <p className="font-body-sm text-body-sm text-on-surface-variant flex items-center gap-sm">
        <IconTile icon="function" size={28} tone="muted" />
        Every figure above was computed by the API against the {int(active?.rows ?? 0)} stored rows of{' '}
        {active?.name ?? 'this file'}.
      </p>
    </PageCanvas>
  )
}

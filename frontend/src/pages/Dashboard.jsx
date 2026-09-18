import { PageCanvas, PageHeader } from '../components/AppShell'
import {
  PanelSkeleton, ErrorState, IconTile, Panel, PanelHeader, Skeleton, StatCard, NoProject,
} from '../components/ui'
import Figure from '../components/Figure'
import Icon from '../components/Icon'
import ProjectPicker from '../components/ProjectPicker'
import { api } from '../lib/api'
import { useResource } from '../hooks/useResource'
import { useDatasets, usePageActions } from '../context/AppContext'
import { downloadCsv } from '../lib/csv'
import { bucketLabel, int } from '../lib/format'

/*
 * The dashboard is a frame; the API decides what goes in it.
 *
 * Which tiles appear, what each chart plots and is called, which table is shown
 * and what the insights say all come from the uploaded file's own columns — a
 * mark sheet gets marks and attendance, an order export gets revenue and
 * products. Nothing on this screen names a business concept of its own, and a
 * slot the file cannot fill is left out rather than drawn empty.
 */

const insightTones = {
  positive: { wrap: 'border-success/30 bg-success-container/40', icon: 'text-success', glyph: 'check_circle' },
  alert: { wrap: 'border-danger/30 bg-danger-container/40', icon: 'text-danger', glyph: 'warning' },
  neutral: { wrap: 'border-outline-variant bg-surface-container-low', icon: 'text-primary', glyph: 'lightbulb' },
}

const domainIcons = {
  education: 'school',
  sales: 'storefront',
  hr: 'badge',
  general: 'dataset',
}

// Written out in full so Tailwind sees every class it has to generate.
const kpiColumns = {
  1: 'xl:grid-cols-1',
  2: 'xl:grid-cols-2',
  3: 'xl:grid-cols-3',
  4: 'xl:grid-cols-4',
  5: 'xl:grid-cols-5',
  6: 'xl:grid-cols-6',
}

export default function Dashboard() {
  const { datasets, activeId, active, revalidate, loading: libraryLoading } = useDatasets()

  const dashboard = useResource(
    () => (activeId ? api.dashboard(activeId) : Promise.resolve(null)),
    [activeId]
  )

  const data = dashboard.data

  usePageActions(
    () =>
      data
        ? {
            exportLabel: 'Export dashboard figures as CSV',
            onExport: () =>
              downloadCsv(
                `${active?.name ?? 'dataset'}-dashboard`,
                [
                  { label: 'Section', value: (r) => r.section },
                  { label: 'Label', value: (r) => r.label },
                  { label: 'Value', value: (r) => r.value },
                ],
                [
                  ...data.kpis.map((k) => ({ section: 'KPI', label: k.label, value: k.value })),
                  ...data.charts.flatMap((c) =>
                    c.figures.map((f) => ({ section: c.title, label: bucketLabel(f.label), value: f.value }))
                  ),
                ]
              ),
          }
        : {},
    [data, active?.name]
  )

  const header = (
    <PageHeader
      title="Dashboard"
      description={
        active
          ? data
            ? `${data.domainLabel} · ${int(active.rows)} rows × ${active.columns} columns, analysed from the file itself.`
            : `${int(active.rows)} rows across ${active.columns} columns.`
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
          <NoProject what="KPIs, charts and insights chosen for your data" />
        </Panel>
      </PageCanvas>
    )
  }

  if (dashboard.error) {
    return (
      <PageCanvas>
        {header}
        <ErrorState
          error={dashboard.error}
          onRetry={async () => {
            // A project can disappear while this screen is open; re-reading the
            // library first drops a selection the server no longer knows about.
            await revalidate()
            dashboard.reload()
          }}
        />
      </PageCanvas>
    )
  }

  if (dashboard.loading || !data) {
    return (
      <PageCanvas>
        {header}
        <Panel className="p-sm px-md flex items-center gap-sm">
          <Icon name="auto_awesome" size={16} className="text-primary animate-pulse" />
          <span className="font-body-main text-body-main text-on-surface-variant">
            Reading the columns and choosing what to measure…
          </span>
        </Panel>
        <div className="grid grid-cols-2 md:grid-cols-3 xl:grid-cols-6 gap-sm">
          {Array.from({ length: 6 }).map((_, i) => (
            <Panel key={i} className="p-md">
              <Skeleton className="h-3 w-20" />
              <Skeleton className="mt-md h-7 w-24" />
            </Panel>
          ))}
        </div>
        <div className="grid grid-cols-12 gap-sm">
          <PanelSkeleton className="col-span-12 lg:col-span-8 h-[300px]" />
          <PanelSkeleton className="col-span-12 lg:col-span-4 h-[300px]" />
        </div>
      </PageCanvas>
    )
  }

  const { kpis, charts, table, insights, quality } = data
  const [first, second, third, fourth] = charts

  return (
    <PageCanvas>
      {header}

      <AnalysisStrip data={data} />

      {kpis.length > 0 && (
        <div className={`grid grid-cols-2 md:grid-cols-3 ${kpiColumns[Math.min(kpis.length, 6)]} gap-sm stagger`}>
          {kpis.map((k) => (
            <StatCard
              key={k.label}
              label={k.label}
              value={k.value}
              icon={k.icon}
              iconTone={k.iconTone}
              delta={k.delta}
              deltaTone={k.deltaTone}
              hint={k.hint}
            />
          ))}
        </div>
      )}

      {first && (
        <div className="grid grid-cols-12 gap-sm">
          <ChartPanel chart={first} className={second ? 'col-span-12 lg:col-span-8' : 'col-span-12'} tall />
          {second && <ChartPanel chart={second} className="col-span-12 lg:col-span-4" tall />}
        </div>
      )}

      <div className="grid grid-cols-12 gap-sm">
        {table && <TablePanel table={table} className={`col-span-12 ${third ? 'xl:col-span-5' : 'xl:col-span-7'}`} />}
        {third && (
          <ChartPanel chart={third} className={`col-span-12 md:col-span-6 ${table ? 'xl:col-span-3' : 'xl:col-span-7'}`} />
        )}
        <InsightsPanel
          insights={insights}
          className={`col-span-12 ${third ? 'md:col-span-6' : ''} ${
            table && third ? 'xl:col-span-4' : table || third ? 'xl:col-span-5' : ''
          }`}
        />
      </div>

      <div className="grid grid-cols-12 gap-sm">
        <QualityPanel quality={quality} className={fourth ? 'col-span-12 xl:col-span-8' : 'col-span-12'} />
        {fourth && <ChartPanel chart={fourth} className="col-span-12 xl:col-span-4" />}
      </div>

      <p className="font-body-sm text-body-sm text-on-surface-variant flex items-center gap-sm">
        <IconTile icon="function" size={28} tone="muted" />
        Every figure above was computed from the {int(quality.rows)} rows and {quality.columns} columns of{' '}
        {active?.name ?? 'this file'}. Figures the file cannot support are left out, not shown as zero.
      </p>
    </PageCanvas>
  )
}

/** What the analyser took the file to be, and which columns told it so. */
function AnalysisStrip({ data }) {
  return (
    <Panel className="px-md py-sm flex flex-col lg:flex-row lg:items-center gap-sm">
      <div className="flex items-center gap-sm min-w-0 lg:max-w-[46%]">
        <span className="w-7 h-7 rounded-lg bg-primary-fixed text-primary flex items-center justify-center shrink-0">
          <Icon name={domainIcons[data.domain] ?? 'dataset'} size={16} />
        </span>
        <div className="min-w-0">
          <p className="font-label-bold text-label-bold text-on-surface flex items-center gap-xs">
            <Icon name="auto_awesome" size={13} className="text-primary" />
            {data.domainLabel}
          </p>
          <p className="font-body-sm text-body-sm text-on-surface-variant">{data.summary}</p>
        </div>
      </div>
      {data.fields.length > 0 && (
        <ul className="flex flex-wrap gap-xs lg:ml-auto lg:justify-end" aria-label="Recognised columns">
          {data.fields.slice(0, 12).map((f) => (
            <li
              key={f.column}
              className="inline-flex items-center gap-1 rounded border border-outline-variant bg-surface-container-low px-1.5 py-[1px] font-body-sm text-[11px] leading-[16px]"
              title={`${f.column} is read as ${f.role.toLowerCase()}`}
            >
              <span className="font-code text-on-surface">{f.column}</span>
              <Icon name="arrow_right_alt" size={11} className="text-outline" />
              <span className="text-primary">{f.role}</span>
            </li>
          ))}
        </ul>
      )}
    </Panel>
  )
}

function ChartPanel({ chart, className = '', tall = false }) {
  return (
    <Panel className={`p-md flex flex-col min-w-0 animate-fade-up ${className}`}>
      <PanelHeader
        title={chart.title}
        action={
          chart.caption && (
            <span className="font-body-sm text-body-sm text-on-surface-variant text-right">{chart.caption}</span>
          )
        }
      />
      <div className="flex-1 mt-md flex items-center">
        <Figure kind={chart.kind} figures={chart.figures} unit={chart.unit} height={tall ? 220 : 180} id={chart.id} ariaLabel={chart.title} />
      </div>
    </Panel>
  )
}

function TablePanel({ table, className = '' }) {
  return (
    <Panel className={`p-md flex flex-col min-w-0 ${className}`}>
      <PanelHeader
        title={table.title}
        action={
          table.caption && <span className="font-body-sm text-body-sm text-on-surface-variant">{table.caption}</span>
        }
      />
      <div className="mt-md overflow-x-auto">
        <table className="w-full text-left border-collapse">
          <thead>
            <tr className="bg-surface-container-low">
              {table.columns.map((c, i) => (
                <th
                  key={`${c.label}-${i}`}
                  scope="col"
                  className={`font-label-bold text-stat-label uppercase text-on-surface-variant py-sm px-sm align-bottom ${
                    c.align === 'right' ? 'text-right' : ''
                  } ${i === 0 ? 'rounded-l-lg' : ''} ${i === table.columns.length - 1 ? 'rounded-r-lg' : ''}`}
                >
                  {c.label}
                </th>
              ))}
            </tr>
          </thead>
          <tbody className="divide-y divide-outline-variant/50">
            {table.rows.map((row, r) => (
              <tr
                key={r}
                className="hover:bg-surface-container-low/60 transition-colors animate-fade-up"
                style={{ animationDelay: `${r * 40}ms` }}
              >
                {row.map((cell, i) => {
                  const right = table.columns[i]?.align === 'right'
                  return (
                    <td
                      key={i}
                      className={`py-sm px-sm ${
                        right
                          ? 'text-right font-code text-code text-on-surface tabular-nums whitespace-nowrap'
                          : 'font-body-main text-body-main text-on-surface max-w-[180px] truncate'
                      }`}
                      title={right ? undefined : cell}
                    >
                      {cell}
                    </td>
                  )
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </Panel>
  )
}

function InsightsPanel({ insights, className = '' }) {
  return (
    <Panel className={`p-md flex flex-col min-w-0 ${className}`}>
      <PanelHeader title="AI Insights" icon="auto_awesome" />
      <ul className="mt-md flex flex-col gap-xs">
        {insights.map((insight, index) => {
          const tone = insightTones[insight.tone] ?? insightTones.neutral
          return (
            <li
              key={insight.title}
              className={`border rounded-lg p-sm px-md flex gap-sm animate-fade-up ${tone.wrap}`}
              style={{ animationDelay: `${index * 70}ms` }}
            >
              <Icon name={insight.icon || tone.glyph} size={16} className={`${tone.icon} shrink-0 mt-[2px]`} />
              <p className="font-body-main text-body-main text-on-surface">
                <span className="font-label-bold">{insight.title}. </span>
                {insight.body}
              </p>
            </li>
          )
        })}
      </ul>
    </Panel>
  )
}

const gradeTone = {
  High: 'bg-success-container text-success',
  Medium: 'bg-primary-fixed text-primary',
  Low: 'bg-danger-container text-danger',
}

function QualityPanel({ quality, className = '' }) {
  const stats = [
    { label: 'Total rows', value: int(quality.rows), icon: 'table_rows' },
    { label: 'Total columns', value: int(quality.columns), icon: 'view_column' },
    { label: 'Missing values', value: int(quality.missingCells), icon: 'block', bad: quality.missingCells > 0 },
    { label: 'Duplicate rows', value: int(quality.duplicateRows), icon: 'content_copy', bad: quality.duplicateRows > 0 },
    { label: 'Completeness', value: `${(quality.completeness * 100).toFixed(1)}%`, icon: 'donut_large' },
  ]

  return (
    <Panel className={`p-md flex flex-col min-w-0 ${className}`}>
      <PanelHeader
        title="Data Quality"
        icon="verified"
        action={
          <span className={`rounded px-1.5 py-[1px] font-label-bold text-[11px] ${gradeTone[quality.grade] ?? gradeTone.Medium}`}>
            {quality.grade}
          </span>
        }
      />

      <div className="mt-md grid grid-cols-1 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)] gap-md">
        <div className="flex flex-col gap-sm">
          <div className="flex items-end gap-sm">
            <span className="font-kpi-value text-stat-value text-on-surface tabular-nums">{quality.score}</span>
            <span className="font-body-sm text-body-sm text-on-surface-variant pb-[3px]">/ 100 quality score</span>
          </div>
          <span className="h-[6px] rounded bg-surface-container-highest overflow-hidden" aria-hidden="true">
            <span
              className={`block h-full rounded origin-left animate-widen ${
                quality.grade === 'Low' ? 'bg-error' : quality.grade === 'Medium' ? 'bg-primary' : 'bg-success'
              }`}
              style={{ width: `${quality.score}%` }}
            />
          </span>
          <dl className="grid grid-cols-1 sm:grid-cols-2 gap-x-md">
            {stats.map((s) => (
              <div key={s.label} className="flex items-center justify-between gap-sm py-[5px] border-b border-outline-variant/60 min-w-0">
                <dt className="flex items-center gap-xs font-body-sm text-body-sm text-on-surface-variant whitespace-nowrap">
                  <Icon name={s.icon} size={13} className="text-outline" />
                  {s.label}
                </dt>
                <dd className={`font-code text-code tabular-nums ${s.bad ? 'text-danger' : 'text-on-surface'}`}>{s.value}</dd>
              </div>
            ))}
          </dl>
          <p className="font-body-sm text-[11px] text-on-surface-variant">
            Score weighs completeness, duplicate rows and values that do not match their column's type.
          </p>
        </div>

        <div className="min-w-0">
          <h4 className="font-label-bold text-stat-label uppercase text-on-surface-variant">Columns with gaps</h4>
          {quality.issues.length === 0 ? (
            <p className="mt-sm flex items-center gap-xs font-body-main text-body-main text-on-surface-variant">
              <Icon name="check_circle" size={15} className="text-success" />
              Every column is fully populated.
            </p>
          ) : (
            <ul className="mt-sm flex flex-col gap-xs">
              {quality.issues.map((issue) => (
                <li key={issue.column} className="flex items-center gap-sm">
                  <span className="font-code text-code text-on-surface w-[96px] shrink-0 truncate" title={issue.column}>
                    {issue.column}
                  </span>
                  <span className="flex-1 h-[8px] rounded bg-danger-container overflow-hidden">
                    <span className="block h-full rounded bg-success" style={{ width: `${issue.completeness * 100}%` }} />
                  </span>
                  <span className="font-code text-code text-on-surface-variant tabular-nums w-[88px] text-right shrink-0">
                    {int(issue.missing)} missing
                  </span>
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>
    </Panel>
  )
}

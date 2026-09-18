import { ColumnChart, DONUT_COLORS, DonutChart, TrackBars, TrendChart } from './charts'
import { bucketLabel, unitValue } from '../lib/format'

/*
 * One chart for any set of figures.
 *
 * Dashboard, Analytics, Reports and the assistant used to each map a chart kind
 * onto a chart component and pick a formatter for the values — and two of them
 * picked dollars whatever the file held. The API says what kind of chart suits
 * the figures and how their values are written; this draws that.
 */

// The words the API and the assistant's specs use, onto the four drawings.
const KINDS = {
  line: 'line',
  trend: 'line',
  columns: 'columns',
  bar: 'columns',
  bars: 'bars',
  ranked: 'bars',
  table: 'bars',
  donut: 'donut',
  share: 'donut',
}

/**
 * @param kind   line | columns | bars | donut (or the aliases above)
 * @param unit   { prefix, suffix, decimals } — how values are written
 * @param height the drawing's height in pixels
 */
export default function Figure({ kind, figures, unit, height = 200, id = 'figure', ariaLabel = 'Chart', fit = true }) {
  if (!figures?.length) {
    return <p className="font-body-sm text-body-sm text-on-surface-variant">Nothing to plot.</p>
  }

  const format = (v) => unitValue(v, unit)
  let drawn = KINDS[kind] ?? 'columns'

  // Four hues stay distinguishable; past that a donut repeats colours, and a
  // column chart of long labels truncates every one of them. A kind the reader
  // picked by hand (fit={false}) is drawn as asked.
  if (fit && drawn === 'donut' && figures.length > DONUT_COLORS.length) drawn = 'bars'
  if (fit && drawn === 'columns' && figures.length > 12) drawn = 'bars'

  if (drawn === 'line') {
    return (
      <div className="flex w-full" style={{ height }}>
        <TrendChart
          points={figures}
          format={format}
          className="flex-1"
          gradientId={`fig-${String(id).replace(/[^a-z0-9]/gi, '')}`}
          ariaLabel={ariaLabel}
        />
      </div>
    )
  }

  if (drawn === 'columns') {
    return (
      <div className="w-full min-w-0">
        <ColumnChart points={figures} format={format} height={height} wideLabels />
      </div>
    )
  }

  if (drawn === 'donut') {
    const total = figures.reduce((sum, f) => sum + f.value, 0)
    const share = (v) => (total ? `${((v / total) * 100).toFixed(1)}%` : '—')
    return (
      <div className="w-full flex flex-col items-center justify-center gap-md" style={{ minHeight: height }}>
        <DonutChart
          segments={figures}
          centerLabel={bucketLabel(figures[0].label)}
          centerValue={share(figures[0].value)}
          size={height >= 220 ? 'w-40 h-40' : 'w-32 h-32'}
          format={format}
        />
        <ul className="flex flex-wrap justify-center gap-x-md gap-y-xs">
          {figures.map((f, i) => (
            <li key={f.label} className="flex items-center gap-xs font-body-sm text-body-sm text-on-surface-variant">
              <span className="w-2 h-2 rounded-sm" style={{ background: DONUT_COLORS[i % DONUT_COLORS.length] }} />
              {bucketLabel(f.label)}
              <span className="font-code text-on-surface tabular-nums">{share(f.value)}</span>
            </li>
          ))}
        </ul>
      </div>
    )
  }

  return (
    <div className="w-full flex items-center" style={{ minHeight: Math.min(height, 40 * figures.length) }}>
      <div className="w-full">
        <TrackBars items={fit ? figures.slice(0, 12) : figures} format={format} showValues />
      </div>
    </div>
  )
}

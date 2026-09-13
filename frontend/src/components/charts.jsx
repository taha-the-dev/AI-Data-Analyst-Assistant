import { useEffect, useRef, useState } from 'react'
import { bucketLabel } from '../lib/format'

/**
 * Charts are drawn as inline SVG rather than pulled from a library: the design
 * calls for a 2px stroke, a dashed 3-line grid and the blue-to-transparent
 * fill, and hand-drawing it keeps the bundle free of a charting dependency.
 *
 * Every chart is hoverable. A chart nobody can read a value off is decoration,
 * and this product is for people who need the number.
 */

const W = 400
const H = 100

const prefersReducedMotion = () =>
  typeof window !== 'undefined' && window.matchMedia?.('(prefers-reduced-motion: reduce)').matches

/**
 * Walks a stroked path on from nothing.
 *
 * The length has to be measured rather than guessed — a dash pattern longer or
 * shorter than the path either never finishes or finishes early — so this reads
 * it off the element and animates the offset back to zero.
 */
function useDrawIn(dependency) {
  const ref = useRef(null)

  useEffect(() => {
    const path = ref.current
    if (!path) return
    if (prefersReducedMotion()) {
      path.style.strokeDasharray = 'none'
      return
    }

    const length = path.getTotalLength()
    path.style.transition = 'none'
    path.style.strokeDasharray = `${length}`
    path.style.strokeDashoffset = `${length}`
    // Forces the starting state to be painted before the transition begins.
    void path.getBoundingClientRect()
    path.style.transition = 'stroke-dashoffset 900ms cubic-bezier(0.16, 1, 0.3, 1)'
    path.style.strokeDashoffset = '0'
  }, [dependency])

  return ref
}

function toPoints(values, width = W, height = H, pad = 6) {
  const max = Math.max(...values)
  const min = Math.min(...values)
  const span = max - min || 1
  const step = values.length > 1 ? width / (values.length - 1) : width
  return values.map((v, i) => [
    +(i * step).toFixed(2),
    +(height - pad - ((v - min) / span) * (height - pad * 2)).toFixed(2),
  ])
}

/** Catmull-Rom → cubic bezier, so the line curves the way the mock does. */
function smoothPath(pts) {
  if (pts.length < 2) return ''
  let d = `M${pts[0][0]},${pts[0][1]}`
  for (let i = 0; i < pts.length - 1; i++) {
    const p0 = pts[i - 1] || pts[i]
    const p1 = pts[i]
    const p2 = pts[i + 1]
    const p3 = pts[i + 2] || p2
    const c1x = p1[0] + (p2[0] - p0[0]) / 6
    const c1y = p1[1] + (p2[1] - p0[1]) / 6
    const c2x = p2[0] - (p3[0] - p1[0]) / 6
    const c2y = p2[1] - (p3[1] - p1[1]) / 6
    d += ` C${c1x.toFixed(2)},${c1y.toFixed(2)} ${c2x.toFixed(2)},${c2y.toFixed(2)} ${p2[0]},${p2[1]}`
  }
  return d
}

export function TrendChart({
  points,
  className = '',
  gradientId = 'trendFill',
  showDots = false,
  showAxis = true,
  format = (v) => v,
  ariaLabel = 'Trend chart',
}) {
  const [hover, setHover] = useState(null)

  const values = points.map((p) => p.value)
  const pts = toPoints(values)
  const line = smoothPath(pts)
  const area = `${line} L${W},${H} L0,${H} Z`
  const lineRef = useDrawIn(line)

  const onMove = (e) => {
    const box = e.currentTarget.getBoundingClientRect()
    const ratio = (e.clientX - box.left) / box.width
    const i = Math.max(0, Math.min(points.length - 1, Math.round(ratio * (points.length - 1))))
    setHover(i)
  }

  // Axis labels thin out so they never collide on a narrow card.
  const labelStep = Math.ceil(points.length / 6)

  return (
    <div className={`flex flex-col ${className}`}>
      <div
        className="relative flex-1 min-h-0"
        onMouseMove={onMove}
        onMouseLeave={() => setHover(null)}
        onTouchStart={onMove}
        onTouchMove={onMove}
        onTouchEnd={() => setHover(null)}
      >
        <svg
          className="w-full h-full block"
          viewBox={`0 0 ${W} ${H}`}
          preserveAspectRatio="none"
          role="img"
          aria-label={`${ariaLabel}. ${points
            .map((p) => `${bucketLabel(p.label)} ${format(p.value)}`)
            .join(', ')}`}
          focusable="false"
        >
          <defs>
            <linearGradient id={gradientId} x1="0" x2="0" y1="0" y2="1">
              <stop offset="0%" stopColor="rgb(var(--chart-line))" stopOpacity="0.2" />
              <stop offset="100%" stopColor="rgb(var(--chart-line))" stopOpacity="0" />
            </linearGradient>
          </defs>

          {[25, 50, 75].map((y) => (
            <line
              key={y}
              x1="0"
              x2={W}
              y1={y}
              y2={y}
              stroke="rgb(var(--chart-grid))"
              strokeDasharray="4"
              strokeWidth="1"
              vectorEffect="non-scaling-stroke"
            />
          ))}

          <path d={area} fill={`url(#${gradientId})`} className="animate-fade-in" />
          <path
            ref={lineRef}
            d={line}
            fill="none"
            stroke="rgb(var(--chart-line))"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
            vectorEffect="non-scaling-stroke"
          />

          {showDots &&
            !hover &&
            [pts[Math.floor(pts.length * 0.6)], pts[pts.length - 1]].map((p, i) => (
              <circle
                key={i}
                cx={p[0]}
                cy={p[1]}
                r="4"
                fill="rgb(var(--chart-dot))"
                stroke="rgb(var(--chart-line))"
                strokeWidth="2"
                vectorEffect="non-scaling-stroke"
              />
            ))}

          {hover !== null && (
            <>
              <line
                x1={pts[hover][0]}
                x2={pts[hover][0]}
                y1="0"
                y2={H}
                stroke="rgb(var(--chart-line))"
                strokeWidth="1"
                strokeDasharray="3 3"
                vectorEffect="non-scaling-stroke"
              />
              <circle
                cx={pts[hover][0]}
                cy={pts[hover][1]}
                r="4"
                fill="rgb(var(--chart-dot))"
                stroke="rgb(var(--chart-line))"
                strokeWidth="2"
                vectorEffect="non-scaling-stroke"
              />
            </>
          )}
        </svg>

        {hover !== null && (
          <div
            className="pointer-events-none absolute top-1 z-10 -translate-x-1/2 rounded-lg border border-outline-variant bg-surface-container-lowest px-2 py-1 ambient-shadow whitespace-nowrap"
            style={{
              left: `${Math.min(88, Math.max(12, (hover / (points.length - 1)) * 100))}%`,
            }}
          >
            <p className="font-body-sm text-body-sm text-on-surface-variant">{bucketLabel(points[hover].label)}</p>
            <p className="font-code text-label-bold text-on-surface tabular-nums">
              {format(points[hover].value)}
            </p>
          </div>
        )}
      </div>

      {showAxis && (
        <div className="flex justify-between px-1 pt-sm font-body-sm text-body-sm text-on-surface-variant">
          {points
            .filter((_, i) => i % labelStep === 0)
            .map((p) => (
              <span key={p.label}>{bucketLabel(p.label)}</span>
            ))}
        </div>
      )}
    </div>
  )
}

// Four hues that stay distinguishable in both themes; the channels swap
// with the theme, the roles do not.
const DONUT_COLORS = [
  'rgb(var(--chart-1))',
  'rgb(var(--chart-2))',
  'rgb(var(--chart-3))',
  'rgb(var(--chart-4))',
]

export function DonutChart({ segments, centerLabel, centerValue, size = 'w-32 h-32', format }) {
  const [hover, setHover] = useState(null)
  const total = segments.reduce((s, x) => s + x.value, 0) || 1
  let offset = 25 // starts the first segment at 12 o'clock

  const shown = hover !== null ? segments[hover] : null

  return (
    <div className="relative flex items-center justify-center">
      <svg
        className={size}
        viewBox="0 0 36 36"
        role="img"
        aria-label={segments
          .map((s) => `${bucketLabel(s.label)} ${Math.round((s.value / total) * 100)}%`)
          .join(', ')}
      >
        <circle cx="18" cy="18" fill="transparent" r="15.915" stroke="rgb(var(--chart-track))" strokeWidth="3" />
        {segments.map((s, i) => {
          const pct = (s.value / total) * 100
          const dash = `${pct.toFixed(2)} ${(100 - pct).toFixed(2)}`
          const el = (
            <circle
              key={s.label}
              cx="18"
              cy="18"
              fill="transparent"
              r="15.915"
              stroke={DONUT_COLORS[i % DONUT_COLORS.length]}
              strokeDasharray={dash}
              strokeDashoffset={offset}
              strokeWidth={hover === i ? 5.5 : 4}
              style={{ animationDelay: `${i * 90}ms` }}
              className="transition-[stroke-width] duration-150 cursor-pointer animate-fade-in"
              onMouseEnter={() => setHover(i)}
              onMouseLeave={() => setHover(null)}
            />
          )
          offset -= pct
          return el
        })}
      </svg>
      <div className="absolute inset-0 flex items-center justify-center flex-col pointer-events-none text-center px-4">
        <span className="font-label-bold text-label-bold text-on-surface truncate max-w-full">
          {shown ? bucketLabel(shown.label) : centerLabel}
        </span>
        <span className="font-code text-body-sm text-on-surface-variant tabular-nums">
          {shown
            ? format
              ? format(shown.value)
              : `${Math.round((shown.value / total) * 100)}%`
            : centerValue}
        </span>
      </div>
    </div>
  )
}

/** Horizontal ranked bars — used where labels are long. */
export function RankedBars({ items, format = (v) => v }) {
  const max = Math.max(...items.map((i) => i.value)) || 1
  return (
    <ul className="flex flex-col gap-sm">
      {items.map((item, i) => (
        <li key={item.label} className="flex items-center gap-sm sm:gap-md group">
          <span className="font-body-sm text-body-sm text-on-surface-variant w-[72px] sm:w-[96px] shrink-0 truncate">
            {bucketLabel(item.label)}
          </span>
          <div className="flex-1 min-w-[40px] h-[14px] bg-surface-container-low rounded overflow-hidden">
            <div
              className={`h-full rounded transition-colors ${
                i === 0 ? 'bg-primary-container' : 'bg-primary-fixed-dim'
              } group-hover:bg-primary-hover`}
              style={{ width: `${(item.value / max) * 100}%` }}
            />
          </div>
          <span className="font-code text-code text-on-surface tabular-nums w-[56px] sm:w-[76px] text-right shrink-0">
            {format(item.value)}
          </span>
        </li>
      ))}
    </ul>
  )
}

/**
 * Label, track, fill — the "Sales by Category" list. The fill steps down in
 * opacity by rank so the leader reads first without a legend.
 */
export function TrackBars({ items, format = (v) => v }) {
  const max = Math.max(...items.map((i) => i.value)) || 1
  return (
    <ul className="flex flex-col gap-sm">
      {items.map((item, i) => (
        <li key={item.label} className="flex items-center gap-md group">
          <span className="font-body-main text-body-main text-on-surface-variant w-[76px] shrink-0 truncate">
            {bucketLabel(item.label)}
          </span>
          <span className="flex-1 h-[10px] rounded bg-surface-container-highest overflow-hidden">
            <span
              className="block h-full rounded bg-primary origin-left animate-widen"
              style={{
                width: `${Math.max(2, (item.value / max) * 100)}%`,
                opacity: Math.max(0.35, 1 - i * 0.17),
                animationDelay: `${Math.min(i, 8) * 60}ms`,
              }}
            />
          </span>
          <span className="font-code text-code text-on-surface tabular-nums w-[64px] text-right shrink-0 opacity-0 group-hover:opacity-100 transition-opacity">
            {format(item.value)}
          </span>
        </li>
      ))}
    </ul>
  )
}

/**
 * Vertical columns with a value axis and dashed gridlines — the Analytics
 * trend. Drawn with flex boxes rather than SVG so the labels stay crisp and
 * the hover target is a real element.
 */
export function ColumnChart({ points, format = (v) => v, height = 210 }) {
  const max = Math.max(...points.map((p) => p.value)) || 1
  // A round number above the tallest column, so the axis reads sensibly.
  const step = Math.pow(10, Math.floor(Math.log10(max))) / 2
  const ceiling = Math.ceil(max / step) * step
  const ticks = [1, 0.75, 0.5, 0.25, 0].map((f) => ceiling * f)

  return (
    <div className="flex gap-sm" style={{ height }}>
      <div className="flex flex-col justify-between py-1 shrink-0">
        {ticks.map((t) => (
          <span key={t} className="font-body-sm text-body-sm text-on-surface-variant tabular-nums">
            {format(t)}
          </span>
        ))}
      </div>

      <div className="flex-1 min-w-0 flex flex-col">
        <div className="relative flex-1 border-l border-b border-outline-variant">
          <div className="absolute inset-0 flex flex-col justify-between pointer-events-none">
            {ticks.map((t) => (
              <span key={t} className="border-t border-dashed border-outline-variant/60" />
            ))}
          </div>

          <div className="absolute inset-0 flex items-end justify-around gap-1 px-2">
            {points.map((p) => (
              <div key={p.label} className="relative group flex-1 max-w-[38px] h-full flex items-end justify-center">
                <span className="pointer-events-none absolute -top-1 z-10 whitespace-nowrap rounded bg-inverse-surface px-1.5 py-0.5 font-body-sm text-[12px] text-inverse-on-surface opacity-0 group-hover:opacity-100 transition-opacity">
                  {bucketLabel(p.label)} · {format(p.value)}
                </span>
                <span
                  className="w-[10px] rounded-t-sm bg-primary-container transition-colors group-hover:bg-primary origin-bottom animate-grow"
                  style={{
                    height: `${Math.max(1, (p.value / ceiling) * 100)}%`,
                    animationDelay: `${Math.min(points.indexOf(p), 12) * 45}ms`,
                  }}
                  role="img"
                  aria-label={`${p.label}: ${format(p.value)}`}
                />
              </div>
            ))}
          </div>
        </div>

        <div className="flex justify-around gap-1 px-2 pt-sm">
          {points.map((p) => (
            <span
              key={p.label}
              className="flex-1 max-w-[38px] text-center font-body-sm text-body-sm text-on-surface-variant truncate"
              title={bucketLabel(p.label)}
            >
              {bucketLabel(p.label)}
            </span>
          ))}
        </div>
      </div>
    </div>
  )
}

/** The descending block chart in the assistant's analysis panel. */
export function MiniColumns({ points, format = (v) => v, height = 170 }) {
  const max = Math.max(...points.map((p) => p.value)) || 1
  return (
    <div className="flex items-end gap-2 border-l border-b border-outline-variant pl-2 pb-0" style={{ height }}>
      {points.map((p, i) => (
        <div key={p.label} className="relative group flex-1 h-full flex items-end">
          <span className="pointer-events-none absolute -top-1 left-1/2 -translate-x-1/2 z-10 whitespace-nowrap rounded bg-inverse-surface px-1.5 py-0.5 font-body-sm text-[12px] text-inverse-on-surface opacity-0 group-hover:opacity-100 transition-opacity">
            {bucketLabel(p.label)} · {format(p.value)}
          </span>
          <span
            className="w-full bg-primary origin-bottom animate-grow"
            style={{
              height: `${Math.max(2, (p.value / max) * 100)}%`,
              opacity: Math.max(0.22, 1 - i * 0.2),
              animationDelay: `${i * 60}ms`,
            }}
            role="img"
            aria-label={`${p.label}: ${format(p.value)}`}
          />
        </div>
      ))}
    </div>
  )
}

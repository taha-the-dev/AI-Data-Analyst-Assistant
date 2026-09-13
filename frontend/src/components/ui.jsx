import { Link } from 'react-router-dom'
import Icon from './Icon'

/* --------------------------------------------------------------------------
   Buttons — 8px radius throughout; never pill-shaped.
-------------------------------------------------------------------------- */

/* --------------------------------------------------------------------------
   Buttons.

   Every screen used to write these class strings by hand, which meant the
   density pass had to touch each one and any of them could drift. One
   implementation, four intents, two sizes.
-------------------------------------------------------------------------- */

const buttonBase =
  'inline-flex items-center justify-center gap-sm rounded-lg font-label-bold text-label-bold ' +
  'transition-colors disabled:opacity-40 disabled:cursor-not-allowed whitespace-nowrap'

const buttonVariants = {
  primary: 'bg-primary-container text-white hover:bg-primary-hover',
  secondary:
    'bg-surface-container-lowest border border-outline-variant text-on-surface hover:bg-surface-container-low',
  danger: 'bg-error text-on-error hover:bg-on-error-container',
  // Reads as secondary until hovered, then as the destructive thing it is.
  quiet:
    'bg-surface-container-lowest border border-outline-variant text-on-surface ' +
    'hover:bg-danger-container hover:text-danger hover:border-danger/40',
  ghost: 'text-on-surface-variant hover:bg-surface-container-low',
}

const buttonSizes = {
  sm: 'h-[26px] px-sm',
  md: 'h-[30px] px-md',
}

export function Button({
  variant = 'secondary',
  size = 'md',
  icon,
  className = '',
  children,
  ...rest
}) {
  return (
    <button
      type="button"
      className={`${buttonBase} ${buttonVariants[variant]} ${buttonSizes[size]} ${className}`}
      {...rest}
    >
      {icon && <Icon name={icon} size={size === 'sm' ? 14 : 15} />}
      {children}
    </button>
  )
}

/** Square, icon-only, and never without a label a screen reader can read. */
export function IconButton({ icon, label, tone = 'default', size = 15, className = '', ...rest }) {
  const tones = {
    default: 'text-on-surface-variant hover:bg-surface-container',
    danger: 'text-on-surface-variant hover:bg-danger-container hover:text-danger',
    primary: 'text-on-surface-variant hover:bg-surface-container-low hover:text-primary',
  }

  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      className={`w-7 h-7 flex items-center justify-center rounded transition-colors ${tones[tone]} ${className}`}
      {...rest}
    >
      <Icon name={icon} size={size} />
    </button>
  )
}

/** The +1.2% / -5.4% markers beside a KPI. */

/* --------------------------------------------------------------------------
   Empty and error states.
-------------------------------------------------------------------------- */

export function EmptyState({ icon = 'inbox', title, body, action, tone = 'empty' }) {
  const isError = tone === 'error'
  return (
    <div
      role={isError ? 'alert' : undefined}
      className={`flex flex-col items-center justify-center text-center px-md py-xl gap-sm ${
        isError ? 'border border-error-container bg-error-container/20 rounded-card' : ''
      }`}
    >
      <span
        className={`w-9 h-9 rounded-full flex items-center justify-center ${
          isError ? 'bg-error-container text-error' : 'bg-surface-container text-outline'
        }`}
      >
        <Icon name={isError ? 'error' : icon} size={18} />
      </span>
      <h4 className="font-label-bold text-label-bold text-on-surface mt-xs">{title}</h4>
      {body && <p className="font-body-sm text-body-sm text-on-surface-variant max-w-sm">{body}</p>}
      {action && <div className="mt-sm">{action}</div>}
    </div>
  )
}

/**
 * What every data screen shows before anything has been uploaded.
 *
 * Nothing is seeded, so this is the app's first impression: it has to read as an
 * invitation rather than as a failure, and name the one action that gets past
 * it. The error state is for things that went wrong; an empty library did not.
 */
export function NoProject({ what = 'figures' }) {
  return (
    <div className="flex flex-col items-center justify-center text-center py-xl px-md gap-sm">
      <span className="w-10 h-10 rounded-full bg-primary-fixed text-primary flex items-center justify-center">
        <Icon name="upload_file" size={20} />
      </span>
      <h3 className="font-section-title text-section-title text-on-surface mt-xs">No project yet</h3>
      <p className="font-body-main text-body-main text-on-surface-variant max-w-sm">
        Upload a CSV and this screen fills with {what} computed from its rows.
      </p>
      <Link
        to="/datasets"
        className="mt-sm inline-flex items-center gap-sm h-[30px] px-md rounded-lg bg-primary-container text-white font-label-bold text-label-bold hover:bg-primary-hover transition-colors"
      >
        <Icon name="upload" size={15} />
        Upload a file
      </Link>
    </div>
  )
}

export function Skeleton({ className = 'h-4 w-full' }) {
  return <div className={`skeleton ${className}`} aria-hidden="true" />
}

/* --------------------------------------------------------------------------
   The two states every screen shares once it reads from the API.
-------------------------------------------------------------------------- */

/**
 * An error says what happened and what to do about it. The API's
 * ProblemDetails already carries both, so they are shown rather than replaced
 * with a generic message.
 */
export function ErrorState({ error, onRetry, className = '' }) {
  return (
    <div className={className}>
      <EmptyState
        tone="error"
        title={error?.title ?? 'Something went wrong'}
        body={error?.detail || 'The request did not complete. Try again in a moment.'}
        action={
          onRetry ? (
            <Button variant="secondary" icon="refresh" onClick={onRetry}>
              Try again
            </Button>
          ) : null
        }
      />
    </div>
  )
}

export function PanelSkeleton({ className = 'h-[280px]' }) {
  return (
    <Panel className={`p-md ${className}`} aria-hidden="true">
      <Skeleton className="h-4 w-40" />
      <Skeleton className="mt-md h-[70%] w-full" />
    </Panel>
  )
}

export function TableSkeleton({ rows = 6, className = '' }) {
  return (
    <div className={`p-md ${className}`} role="status" aria-label="Loading">
      {Array.from({ length: rows }).map((_, i) => (
        <div key={i} className="flex items-center gap-md py-sm">
          <Skeleton className="h-8 w-8 rounded" />
          <Skeleton className="h-3 flex-1" />
          <Skeleton className="h-3 w-16 hidden sm:block" />
          <Skeleton className="h-3 w-20 hidden md:block" />
        </div>
      ))}
    </div>
  )
}

/* --------------------------------------------------------------------------
   The KPI tile from the source screens: uppercase caption, muted glyph, one
   large number, and a tinted delta chip when the API sends a delta.
-------------------------------------------------------------------------- */

export function StatCard({ label, value, icon, delta, deltaTone = 'up', iconTone = 'muted' }) {
  const down = deltaTone === 'down'
  return (
    <div className="bg-surface-container-lowest border border-outline-variant rounded-lg p-sm flex flex-col gap-xs">
      <div className="flex items-start justify-between gap-sm">
        <span className="font-label-bold text-stat-label uppercase text-on-surface-variant">{label}</span>
        {icon && (
          <Icon name={icon} size={14} className={iconTone === 'error' ? 'text-error' : 'text-outline'} />
        )}
      </div>
      <span className="font-kpi-value text-stat-value text-on-surface tabular-nums">{value}</span>
      {delta && (
        <span
          title="Latest period against the one before it"
          className={`inline-flex items-center gap-1 self-start rounded px-1 py-[1px] font-label-bold text-[11px] ${
            down ? 'bg-danger-container text-danger' : 'bg-success-container text-success'
          }`}
        >
          <Icon name={down ? 'trending_down' : 'trending_up'} size={12} />
          {delta}
        </span>
      )}
    </div>
  )
}

/* --------------------------------------------------------------------------
   Panels
-------------------------------------------------------------------------- */

/** White panel with the design's 12px radius and hairline border. */
export function Panel({ className = '', children, ...rest }) {
  return (
    <section
      className={`bg-surface-container-lowest border border-outline-variant rounded-lg ${className}`}
      {...rest}
    >
      {children}
    </section>
  )
}

export function PanelHeader({ title, icon, action, className = '' }) {
  return (
    <div className={`flex items-center justify-between gap-md ${className}`}>
      <h3 className="font-section-title text-section-title text-on-surface flex items-center gap-sm">
        {icon && <Icon name={icon} size={15} className="text-primary" />}
        {title}
      </h3>
      {action}
    </div>
  )
}

/* --------------------------------------------------------------------------
   Small controls shared by the toolbars
-------------------------------------------------------------------------- */

export function Select({ label, value, onChange, options, className = '' }) {
  return (
    <label className={`inline-flex items-center gap-sm ${className}`}>
      {label && (
        <span className="font-label-bold text-stat-label uppercase text-on-surface-variant whitespace-nowrap">
          {label}
        </span>
      )}
      <span className="relative">
        <select
          value={value}
          onChange={(e) => onChange(e.target.value)}
          className="appearance-none h-[30px] pl-2 pr-7 rounded-lg border border-outline-variant bg-surface-container-lowest font-body-main text-body-main text-on-surface hover:bg-surface-container-low focus:outline-none focus:border-primary-container transition-colors"
        >
          {options.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </select>
        <Icon
          name="expand_more"
          size={15}
          className="absolute right-2 top-1/2 -translate-y-1/2 text-on-surface-variant pointer-events-none"
        />
      </span>
    </label>
  )
}

/** Segmented control — the chart-type switch on Analytics. */
export function SegmentedControl({ value, onChange, options, label }) {
  return (
    <div
      role="radiogroup"
      aria-label={label}
      className="inline-flex items-center rounded-lg border border-outline-variant bg-surface-container-lowest p-[2px]"
    >
      {options.map((o) => {
        const active = o.value === value
        return (
          <button
            key={o.value}
            role="radio"
            aria-checked={active}
            aria-label={o.label}
            title={o.label}
            onClick={() => onChange(o.value)}
            className={`h-[28px] flex items-center justify-center gap-xs rounded transition-colors ${
              o.icon ? 'w-[32px]' : 'px-md font-label-bold text-label-bold'
            } ${
              active
                ? 'bg-primary-fixed text-primary'
                : 'text-on-surface-variant hover:bg-surface-container-low'
            }`}
          >
            {/* An option with an icon shows only the icon, as in the comps.
                One without shows its label, for choices whose glyphs would be
                a guess — Light and Dark read as words, not pictograms. */}
            {o.icon ? <Icon name={o.icon} size={15} /> : o.label}
          </button>
        )
      })}
    </div>
  )
}

const statusTones = {
  Ready: 'bg-success-container text-success',
  High: 'bg-success-container text-success',
  Medium: 'bg-warning-container/50 text-on-warning-container',
  Syncing: 'bg-warning-container/50 text-on-warning-container',
  Draft: 'bg-warning-container/50 text-on-warning-container',
  Low: 'bg-danger-container text-danger',
  Failed: 'bg-danger-container text-danger',
  'Source deleted': 'bg-danger-container text-danger',
}

/** The pill beside a file or a report. Falls back to a neutral tone. */
export function StatusChip({ status, className = '' }) {
  return (
    <span
      className={`inline-flex items-center rounded px-1.5 py-[1px] font-label-bold text-[11px] ${
        statusTones[status] ?? 'bg-surface-container-high text-on-surface-variant'
      } ${className}`}
    >
      {status}
    </span>
  )
}

/** Rounded square glyph that fronts a row in the lists. */
export function IconTile({ icon, size = 40, tone = 'primary' }) {
  return (
    <span
      style={{ width: size, height: size }}
      className={`rounded-lg flex items-center justify-center shrink-0 ${
        tone === 'primary' ? 'bg-primary-fixed text-primary' : 'bg-surface-container text-on-surface-variant'
      }`}
    >
      <Icon name={icon} size={Math.round(size * 0.5)} />
    </span>
  )
}

export function Pagination({ page, pageCount, onPage, summary }) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-sm">
      <p className="font-body-sm text-body-sm text-on-surface-variant">{summary}</p>
      <div className="flex items-center gap-xs">
        <button
          onClick={() => onPage(page - 1)}
          disabled={page <= 1}
          aria-label="Previous page"
          className="w-7 h-7 flex items-center justify-center rounded border border-outline-variant text-on-surface-variant hover:bg-surface-container-low transition-colors disabled:opacity-40"
        >
          <Icon name="chevron_left" size={15} />
        </button>
        <span className="font-body-sm text-body-sm text-on-surface tabular-nums px-2">{page}</span>
        <button
          onClick={() => onPage(page + 1)}
          disabled={page >= pageCount}
          aria-label="Next page"
          className="w-7 h-7 flex items-center justify-center rounded border border-outline-variant text-on-surface-variant hover:bg-surface-container-low transition-colors disabled:opacity-40"
        >
          <Icon name="chevron_right" size={15} />
        </button>
      </div>
    </div>
  )
}

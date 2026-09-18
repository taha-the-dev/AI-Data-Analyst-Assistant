export const int = (n) => n.toLocaleString('en-US')

/**
 * A figure in the unit the API described for it — `{ prefix: '$', suffix: '',
 * decimals: 0 }` — shortened to K / M / B once it gets long. The API decides
 * the unit from the file, so a marks chart never grows a dollar sign.
 */
export function unitValue(n, unit = {}, { compact = true } = {}) {
  const { prefix = '', suffix = '', decimals = 0 } = unit
  const abs = Math.abs(n)
  let body
  if (compact && abs >= 1e9) body = `${+(n / 1e9).toFixed(2)}B`
  else if (compact && abs >= 1e6) body = `${+(n / 1e6).toFixed(2)}M`
  else if (compact && abs >= 1e4) body = `${+(n / 1e3).toFixed(1)}K`
  else body = n.toLocaleString('en-US', { minimumFractionDigits: decimals, maximumFractionDigits: decimals })
  return `${prefix}${body}${suffix}`
}

const timeFmt = new Intl.DateTimeFormat('en-GB', { hour: '2-digit', minute: '2-digit', hour12: false })
const dayFmt = new Intl.DateTimeFormat('en-GB', { day: '2-digit', month: 'short', year: 'numeric' })

/** "Today, 09:41" / "Yesterday, 14:20" / "24 Oct 2025" — how the design words it. */
export function whenLabel(iso) {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return '—'

  const now = new Date()
  const sameDay = (a, b) =>
    a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate()

  const yesterday = new Date(now)
  yesterday.setDate(now.getDate() - 1)

  if (sameDay(d, now)) return `Today, ${timeFmt.format(d)}`
  if (sameDay(d, yesterday)) return `Yesterday, ${timeFmt.format(d)}`
  return dayFmt.format(d)
}

/** Clock time for a chat turn. */
export function clock(iso) {
  if (!iso) return ''
  const d = new Date(iso)
  return Number.isNaN(d.getTime()) ? '' : timeFmt.format(d)
}

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']

/**
 * How a group label reads on an axis or in a table.
 *
 * The API buckets dates into sortable keys — "2026-01", "2026-Q1", "2026-01-06"
 * — because that is what keeps them in order. Those keys are for the machine;
 * "Jan 2026" is what a person reads. Anything that is not a date key, a region
 * or a product name, comes back untouched.
 */
export function bucketLabel(label) {
  if (typeof label !== 'string') return label

  const month = /^(\d{4})-(0[1-9]|1[0-2])$/.exec(label)
  if (month) return `${MONTHS[Number(month[2]) - 1]} ${month[1]}`

  const quarter = /^(\d{4})[-\s]?Q([1-4])$/i.exec(label)
  if (quarter) return `Q${quarter[2]} ${quarter[1]}`

  const day = /^(\d{4})-(0[1-9]|1[0-2])-(0[1-9]|[12]\d|3[01])$/.exec(label)
  if (day) return `${Number(day[3])} ${MONTHS[Number(day[2]) - 1]} ${day[1]}`

  const week = /^(\d{4})-W(\d{1,2})$/i.exec(label)
  if (week) return `Week ${Number(week[2])}, ${week[1]}`

  return label
}

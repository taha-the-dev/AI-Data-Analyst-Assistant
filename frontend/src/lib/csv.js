/**
 * Client-side CSV export.
 *
 * The Export button in the top bar writes out exactly the figures the screen is
 * showing — the same numbers the API computed — so the file and the page can
 * never disagree.
 */

// A spreadsheet runs a cell that starts with =, +, -, @, a tab or a carriage
// return as a formula. Values come from uploaded files, so a crafted cell could
// execute in whoever opens the export; a leading apostrophe keeps it as text.
// Numbers, negative ones included, are left alone.
const FORMULA_START = /^[=+\-@\t\r]/
const NUMERIC = /^[-+]?\d[\d.,]*%?$/

const cell = (value) => {
  let text = value === null || value === undefined ? '' : String(value)
  if (typeof value !== 'number' && FORMULA_START.test(text) && !NUMERIC.test(text)) text = `'${text}`
  return /[",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text
}

export function toCsv(columns, rows) {
  const head = columns.map((c) => cell(c.label)).join(',')
  const body = rows.map((row) => columns.map((c) => cell(c.value(row))).join(',')).join('\n')
  return `${head}\n${body}\n`
}

export function downloadCsv(filename, columns, rows) {
  const blob = new Blob([toCsv(columns, rows)], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = filename.endsWith('.csv') ? filename : `${filename}.csv`
  document.body.appendChild(link)
  link.click()
  link.remove()
  // Revoked on the next tick so the download has started before the URL dies.
  setTimeout(() => URL.revokeObjectURL(url), 0)
}

/**
 * Appearance: light, dark, or whatever the machine is set to.
 *
 * The choice lives in localStorage rather than in the API, because it belongs
 * to the screen you are reading on and not to the workspace — the same data on
 * a laptop at night and a monitor at noon wants two different answers.
 *
 * `applyAppearance` is the only thing that touches <html>. public/theme-init.js
 * runs the same logic before first paint so a dark reader never gets a white
 * flash on the way in; this module keeps the two in step afterwards.
 */

const KEY = 'datamind.appearance'

export const APPEARANCES = ['light', 'dark', 'system']

const query = () =>
  typeof window !== 'undefined' && window.matchMedia
    ? window.matchMedia('(prefers-color-scheme: dark)')
    : null

/** The stored choice, or 'system' when nothing is stored or storage is blocked. */
export function readAppearance() {
  try {
    const stored = localStorage.getItem(KEY)
    return APPEARANCES.includes(stored) ? stored : 'system'
  } catch {
    return 'system'
  }
}

/** The choice turned into the theme actually painted: 'light' or 'dark'. */
export function resolveAppearance(appearance) {
  if (appearance === 'light' || appearance === 'dark') return appearance
  return query()?.matches ? 'dark' : 'light'
}

/** Stamps the resolved theme on <html> and returns it. */
export function applyAppearance(appearance) {
  const theme = resolveAppearance(appearance)
  const root = document.documentElement
  root.classList.toggle('dark', theme === 'dark')
  root.classList.toggle('light', theme === 'light')
  return theme
}

export function storeAppearance(appearance) {
  try {
    localStorage.setItem(KEY, appearance)
  } catch {
    // A blocked store costs the preference on the next load, nothing more.
  }
  return applyAppearance(appearance)
}

/**
 * Follows the machine while the choice is 'system'. Returns the unsubscribe.
 */
export function watchSystemAppearance(onChange) {
  const mq = query()
  if (!mq) return () => {}
  const handler = () => onChange(resolveAppearance('system'))
  mq.addEventListener('change', handler)
  return () => mq.removeEventListener('change', handler)
}

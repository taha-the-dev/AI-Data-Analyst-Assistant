import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react'
import { api } from '../lib/api'
import {
  applyAppearance,
  readAppearance,
  storeAppearance,
  watchSystemAppearance,
} from '../lib/theme'

/* --------------------------------------------------------------------------
   Active dataset.

   The top bar names the files the way the design does — as tabs — and every
   screen below reads from whichever one is selected. Holding that here means a
   page never has to guess an id, and switching file re-runs every query on the
   screen against the new one.
-------------------------------------------------------------------------- */

const DatasetContext = createContext(null)

const STORAGE_KEY = 'datamind.activeDataset'

/**
 * Drops the remembered project, so the next account to sign in on this browser
 * does not start on an id that was never its own.
 */
export function forgetActiveDataset() {
  try {
    localStorage.removeItem(STORAGE_KEY)
  } catch {
    // Nothing was stored.
  }
}

export function DatasetProvider({ children }) {
  const [datasets, setDatasets] = useState([])

  // The remembered project is a hint, not a fact: it may have been deleted in
  // another tab or through the API since this browser last looked. Screens see
  // null until the library confirms it, which costs one render and saves a 404
  // from every query on the screen when the hint has gone stale.
  const remembered = useRef(
    (() => {
      const stored = Number(localStorage.getItem(STORAGE_KEY))
      return Number.isFinite(stored) && stored > 0 ? stored : null
    })()
  )
  const [activeId, setActiveId] = useState(null)
  const [error, setError] = useState(null)
  const [loading, setLoading] = useState(true)

  // The id the server last stored for this account, so a change is only sent
  // when it is one.
  const saved = useRef(undefined)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      // pageSize covers the whole library: the tab strip scrolls rather than pages.
      // The account's last selection is kept on the server so that signing out
      // and back in opens on the same file; the browser's copy is the fallback
      // for an API that cannot say.
      const [page, workspace] = await Promise.all([
        api.datasets.list({ pageSize: 50, sort: 'updated', dir: 'desc' }),
        saved.current === undefined ? api.workspace.get().catch(() => null) : null,
      ])
      if (workspace) {
        saved.current = workspace.activeDatasetId ?? null
        if (workspace.activeDatasetId) remembered.current = workspace.activeDatasetId
      }
      setDatasets(page.items)
      setError(null)
      setActiveId((current) => {
        const wanted = current ?? remembered.current
        return wanted && page.items.some((d) => d.id === wanted) ? wanted : (page.items[0]?.id ?? null)
      })
    } catch (cause) {
      setError(cause)
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    load()
  }, [load])

  useEffect(() => {
    if (!activeId) return
    localStorage.setItem(STORAGE_KEY, String(activeId))
    if (activeId === saved.current) return
    saved.current = activeId
    // Best effort: failing to remember a tab must not get in the way of using it.
    api.workspace.save(activeId).catch(() => {
      saved.current = undefined
    })
  }, [activeId])

  /**
   * Re-reads the library and drops a selection that no longer exists.
   *
   * The selected project is remembered across reloads, so a project deleted
   * somewhere else — another tab, the API directly — leaves this one pointing at
   * an id the server will refuse. Every screen would then show "Dataset not
   * found" until someone reloaded by hand; instead, a retry heals it.
   */
  const revalidate = useCallback(async () => {
    await load()
  }, [load])

  const value = useMemo(
    () => ({
      datasets,
      activeId,
      active: datasets.find((d) => d.id === activeId) ?? null,
      setActiveId,
      reload: load,
      revalidate,
      error,
      loading,
    }),
    [datasets, activeId, error, loading, load, revalidate]
  )

  return <DatasetContext.Provider value={value}>{children}</DatasetContext.Provider>
}

export function useDatasets() {
  const context = useContext(DatasetContext)
  if (!context) throw new Error('useDatasets must be used inside DatasetProvider')
  return context
}

/* --------------------------------------------------------------------------
   Page actions.

   Filter and Export live in the top bar in the design, but what they act on
   belongs to the screen underneath. A page registers its handlers on mount and
   the bar renders them; a page that registers nothing gets disabled buttons
   rather than buttons that lie.
-------------------------------------------------------------------------- */

const PageActionsContext = createContext(null)

export function PageActionsProvider({ children }) {
  const [actions, setActions] = useState({})
  const value = useMemo(() => ({ actions, setActions }), [actions])
  return <PageActionsContext.Provider value={value}>{children}</PageActionsContext.Provider>
}

export function usePageActionsState() {
  const context = useContext(PageActionsContext)
  if (!context) throw new Error('usePageActionsState must be used inside PageActionsProvider')
  return context
}

/**
 * Registers this screen's top-bar actions. Pass `{ onFilter, onExport, exportLabel }`.
 * `deps` behaves like an effect's dependency list.
 */
export function usePageActions(factory, deps = []) {
  const { setActions } = usePageActionsState()

  useEffect(() => {
    setActions(factory() ?? {})
    return () => setActions({})
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps)
}

/* --------------------------------------------------------------------------
   Appearance.

   Light, dark, or follow the machine. The class on <html> is stamped before
   first paint by public/theme-init.js; this holds the choice so the
   Settings control and the system listener never disagree about it.
-------------------------------------------------------------------------- */

const AppearanceContext = createContext(null)

export function AppearanceProvider({ children }) {
  const [appearance, setAppearance] = useState(readAppearance)
  const [theme, setTheme] = useState(() => applyAppearance(readAppearance()))

  const choose = useCallback((next) => {
    setAppearance(next)
    setTheme(storeAppearance(next))
  }, [])

  // Only while the choice is "system": a machine that flips to dark at sunset
  // should take the page with it, but an explicit choice outranks it.
  useEffect(() => {
    if (appearance !== 'system') return undefined
    return watchSystemAppearance(() => setTheme(applyAppearance('system')))
  }, [appearance])

  const value = useMemo(() => ({ appearance, theme, setAppearance: choose }), [appearance, theme, choose])
  return <AppearanceContext.Provider value={value}>{children}</AppearanceContext.Provider>
}

export function useAppearance() {
  const context = useContext(AppearanceContext)
  if (!context) throw new Error('useAppearance must be used inside AppearanceProvider')
  return context
}

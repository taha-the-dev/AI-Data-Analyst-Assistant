import { useEffect, useState } from 'react'
import { Outlet, useLocation } from 'react-router-dom'
import SideNavBar from './SideNavBar'
import TopNavBar from './TopNavBar'
import { DatasetProvider, PageActionsProvider } from '../context/AppContext'

const TITLES = [
  [/^\/$/, 'Dashboard'],
  [/^\/datasets/, 'Datasets'],
  [/^\/explorer/, 'Data Explorer'],
  [/^\/analytics/, 'Analytics'],
  [/^\/analyst/, 'AI Assistant'],
  [/^\/history/, 'History'],
  [/^\/reports/, 'Reports'],
  [/^\/settings/, 'Settings'],
]

/**
 * Screens that carry their own controls, where the top bar's period, Filter
 * and Export buttons would only repeat them or do nothing.
 */
const SCREENS_WITHOUT_ACTIONS = /^\/(analyst|datasets|settings)(\/|$)/

/** Screens that choose their file from a picker in the bar rather than the tab strip. */
const SCREENS_WITHOUT_TABS = /^\/$/

/**
 * Frame shared by every screen: charcoal rail on the left, docked bar on top,
 * page canvas underneath.
 *
 * Two canvas modes. Most screens scroll the page normally. Data Explorer and
 * the AI Assistant pin to the viewport and scroll inside their own panes, which
 * is what the source design does for both.
 */
export default function AppShell() {
  const [navOpen, setNavOpen] = useState(false)
  const { pathname } = useLocation()

  // Browser tab and history entries name the screen, not just the product.
  useEffect(() => {
    const match = TITLES.find(([re]) => re.test(pathname))
    const screen = match?.[1]
    document.title = screen ? `${screen} — DataMind AI` : 'DataMind AI'
  }, [pathname])

  // Moving to another screen returns focus to the top of the new page rather
  // than leaving it wherever the previous screen left it.
  useEffect(() => {
    document.getElementById('main-content')?.focus({ preventScroll: true })
  }, [pathname])

  return (
    <DatasetProvider>
      <PageActionsProvider>
        <div className="min-h-screen">
          <a className="skip-link" href="#main-content">
            Skip to content
          </a>

          <SideNavBar open={navOpen} onClose={() => setNavOpen(false)} />

          <div className="md:ml-rail flex flex-col min-h-screen min-w-0">
            <TopNavBar
              onOpenNav={() => setNavOpen(true)}
              showActions={!SCREENS_WITHOUT_ACTIONS.test(pathname)}
              showTabs={!SCREENS_WITHOUT_TABS.test(pathname)}
            />
            <Outlet />
          </div>
        </div>
      </PageActionsProvider>
    </DatasetProvider>
  )
}

/** Standard scrolling canvas. */
export function PageCanvas({ children, className = '' }) {
  return (
    <main
      id="main-content"
      tabIndex={-1}
      // `stagger` assembles the screen top-down on arrival; each route renders
      // its own <main>, so the entrance replays on navigation and never on a
      // data refresh.
      className={`stagger flex-1 w-full max-w-content-max mx-auto px-md md:px-lg py-md pb-lg flex flex-col gap-md focus:outline-none ${className}`}
    >
      {children}
    </main>
  )
}

/**
 * Viewport-locked canvas for the two screens that manage their own scrolling.
 * Direction is a prop rather than a class the caller appends: `flex-col` and
 * `flex-row` collide on specificity, and the stylesheet order decides the
 * winner, not the order they appear in `className`.
 */
export function FixedCanvas({ children, row = false, className = '' }) {
  return (
    <main
      id="main-content"
      tabIndex={-1}
      className={`fixed-canvas flex ${row ? 'flex-row' : 'flex-col'} min-w-0 overflow-hidden focus:outline-none ${className}`}
    >
      {children}
    </main>
  )
}

/** Page title block, with an optional action opposite the title. */
export function PageHeader({ title, description, action }) {
  return (
    // The panels below animate in, and an animation on opacity and transform
    // makes its element a stacking context — which paints over anything the
    // header opens, however high its own z-index. Lifting the header settles it.
    <div className="relative z-20 flex flex-wrap justify-between items-start gap-md">
      <div className="min-w-0">
        <h2 className="font-page-title text-page-title text-on-surface">{title}</h2>
        {description && (
          <p className="font-body-main text-body-main text-on-surface-variant mt-xs">{description}</p>
        )}
      </div>
      {action}
    </div>
  )
}

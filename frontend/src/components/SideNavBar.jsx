import { useState } from 'react'
import { NavLink } from 'react-router-dom'
import Icon from './Icon'
import { useToast } from './Toast'
import { useAuth } from '../context/AuthContext'

const NAV_ITEMS = [
  { to: '/', label: 'Dashboard', icon: 'dashboard', end: true },
  { to: '/analyst', label: 'AI Assistant', icon: 'smart_toy' },
  { to: '/datasets', label: 'Datasets', icon: 'database' },
  { to: '/analytics', label: 'Analytics', icon: 'analytics' },
  { to: '/reports', label: 'Reports', icon: 'description' },
  { to: '/history', label: 'History', icon: 'history' },
]

// Settings sits under the divider rather than among NAV_ITEMS. Data Explorer is
// not in the rail at all: it is reached from the Dashboard and Datasets screens.
const SETTINGS_ITEM = { to: '/settings', label: 'Settings', icon: 'settings' }

function NavItem({ item, onNavigate, className }) {
  return (
    <NavLink
      to={item.to}
      end={item.end}
      onClick={onNavigate}
      className={({ isActive }) =>
        [
          'group flex items-center gap-sm rounded-lg h-[36px] px-md transition-colors duration-150',
          isActive
            ? 'bg-nav-active text-white font-label-bold text-label-bold'
            : 'text-secondary-fixed-dim font-body-main text-body-main hover:bg-nav-hover hover:text-white',
          className,
        ].join(' ')
      }
    >
      {({ isActive }) => (
        <>
          <Icon
            name={item.icon}
            size={16}
            fill={isActive}
            className="shrink-0 transition-colors duration-150 group-hover:text-primary"
          />
          <span className="truncate">{item.label}</span>
        </>
      )}
    </NavLink>
  )
}

/** Who is signed in, and the way out. Always at the foot of the rail. */
function AccountRow() {
  const { account, signOut } = useAuth()
  const toast = useToast()
  const [pending, setPending] = useState(false)

  const leave = async () => {
    setPending(true)
    try {
      await signOut()
    } catch (cause) {
      toast(cause?.detail || cause?.title || 'Could not sign out.', 'error')
      setPending(false)
    }
  }

  return (
    <div className="mx-md mb-xs flex items-center gap-sm px-md py-xs">
      <span
        title={account?.email}
        className="min-w-0 flex-1 truncate font-body-sm text-body-sm text-secondary-fixed-dim"
      >
        {account?.email}
      </span>
      <button
        type="button"
        onClick={leave}
        disabled={pending}
        aria-label="Sign out"
        title="Sign out"
        className="w-7 h-7 shrink-0 flex items-center justify-center rounded text-secondary-fixed-dim hover:bg-nav-hover hover:text-white transition-colors disabled:opacity-40"
      >
        <Icon name="logout" size={16} />
      </button>
    </div>
  )
}

export default function SideNavBar({ open, onClose }) {
  return (
    <>
      {open && (
        <div
          className="fixed inset-0 z-40 bg-nav-deep/60 backdrop-blur-[1px] md:hidden"
          onClick={onClose}
          aria-hidden="true"
        />
      )}

      <aside
        className={[
          'on-dark nav-surface w-rail h-screen fixed left-0 top-0 z-50 flex flex-col py-md',
          'transition-transform duration-200 md:translate-x-0',
          open ? 'translate-x-0' : '-translate-x-full',
        ].join(' ')}
        aria-label="Main navigation"
      >
        {/* Header */}
        <div className="flex items-center gap-sm px-md pb-md pt-xs">
          <span
            aria-hidden="true"
            className="w-8 h-8 rounded-lg bg-primary-container text-white flex items-center justify-center font-label-bold text-[13px] shrink-0"
          >
            DM
          </span>
          <div className="min-w-0 flex-1">
            <h1 className="font-page-title text-[14px] leading-[18px] font-semibold text-white truncate">
              DataMind AI
            </h1>
          </div>
          <button
            onClick={onClose}
            aria-label="Close navigation"
            className="md:hidden rounded p-1 text-secondary-fixed-dim hover:bg-nav-hover hover:text-white transition-colors"
          >
            <Icon name="close" size={20} />
          </button>
        </div>

        {/* Navigation items */}
        <nav className="flex flex-col gap-md px-md pt-md overflow-y-auto flex-1">
          {NAV_ITEMS.map((item) => (
            <NavItem key={item.to} item={item} onNavigate={onClose} />
          ))}

          {/* Divider before Settings */}
          <hr className="border-outline-variant my-md" />

          {/* Settings anchored at bottom */}
          <NavItem item={SETTINGS_ITEM} onNavigate={onClose} className="mt-auto" />
        </nav>

        <div className="border-t border-nav-line pt-xs">
          <AccountRow />
        </div>
      </aside>
    </>
  )
}

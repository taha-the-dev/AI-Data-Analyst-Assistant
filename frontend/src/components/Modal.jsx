import { useEffect, useRef } from 'react'
import Icon from './Icon'

export default function Modal({ open, onClose, title, description, children, footer }) {
  const panel = useRef(null)

  useEffect(() => {
    if (!open) return
    const previous = document.activeElement

    const onKey = (e) => {
      if (e.key === 'Escape') {
        onClose()
        return
      }
      if (e.key !== 'Tab' || !panel.current) return
      const items = panel.current.querySelectorAll(
        'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])'
      )
      if (!items.length) return
      const first = items[0]
      const last = items[items.length - 1]
      if (e.shiftKey && document.activeElement === first) {
        e.preventDefault()
        last.focus()
      } else if (!e.shiftKey && document.activeElement === last) {
        e.preventDefault()
        first.focus()
      }
    }

    document.addEventListener('keydown', onKey)
    document.body.style.overflow = 'hidden'
    panel.current?.querySelector('button')?.focus()

    return () => {
      document.removeEventListener('keydown', onKey)
      document.body.style.overflow = ''
      previous?.focus?.()
    }
  }, [open, onClose])

  if (!open) return null

  return (
    <div className="fixed inset-0 z-[60] flex items-center justify-center p-md animate-fade-in">
      {/* A scrim dims the page. inverse-surface flips with the theme, so in dark
          it washed the page lighter instead; nav-deep is the darkest colour in
          both themes. */}
      <div className="absolute inset-0 bg-nav-deep/50" onClick={onClose} aria-hidden="true" />
      <div
        ref={panel}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        className="relative w-full max-w-md max-h-[85vh] flex flex-col bg-surface-container-lowest border border-outline-variant rounded-card ambient-shadow animate-scale-in"
      >
        <div className="flex items-start justify-between gap-md p-lg pb-md shrink-0">
          <div>
            <h2 className="font-section-title text-section-title text-on-surface">{title}</h2>
            {description && (
              <p className="font-body-main text-body-main text-on-surface-variant mt-xs">{description}</p>
            )}
          </div>
          <button
            onClick={onClose}
            aria-label="Close"
            className="text-on-surface-variant hover:bg-surface-container-low p-1 rounded transition-colors shrink-0"
          >
            <Icon name="close" size={20} />
          </button>
        </div>
        {/* A tall dialog scrolls its own body rather than the page — on a short
            phone viewport the footer must stay reachable. */}
        {children && <div className="px-lg pb-md overflow-y-auto">{children}</div>}
        {footer && (
          <div className="flex flex-wrap justify-end gap-sm px-lg py-md border-t border-outline-variant bg-surface-container-low rounded-b-card shrink-0">
            {footer}
          </div>
        )}
      </div>
    </div>
  )
}

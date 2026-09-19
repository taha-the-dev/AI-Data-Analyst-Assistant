import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import Icon from './Icon'
import { useDatasets } from '../context/AppContext'
import { int } from '../lib/format'

/**
 * Which file the screen is reading from, as a control rather than a caption.
 *
 * Every figure on a screen belongs to one project, so the name of that project
 * has to be visible at the point the figures are read — not inferred from a tab
 * somewhere above. Choosing here switches the whole app, because the selection
 * lives in shared context.
 */
export default function ProjectPicker({ className = '' }) {
  const { datasets, activeId, active, setActiveId, loading } = useDatasets()
  const [open, setOpen] = useState(false)
  const ref = useRef(null)

  useEffect(() => {
    if (!open) return
    const onDown = (e) => {
      if (!ref.current?.contains(e.target)) setOpen(false)
    }
    const onKey = (e) => e.key === 'Escape' && setOpen(false)
    document.addEventListener('mousedown', onDown)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onDown)
      document.removeEventListener('keydown', onKey)
    }
  }, [open])

  return (
    <div className={`relative ${className}`} ref={ref}>
      <button
        onClick={() => setOpen((v) => !v)}
        aria-haspopup="listbox"
        aria-expanded={open}
        className="inline-flex items-center gap-sm h-[30px] pl-sm pr-2 rounded-lg border border-outline-variant bg-surface-container-lowest hover:bg-surface-container-low transition-colors max-w-[280px]"
      >
        <Icon name="folder_open" size={15} className="text-primary shrink-0" />
        <span className="font-body-sm text-body-sm text-on-surface-variant shrink-0">Project</span>
        <span className="font-label-bold text-label-bold text-on-surface truncate">
          {loading ? 'Loading…' : (active?.name ?? 'None selected')}
        </span>
        <Icon name="expand_more" size={15} className="text-on-surface-variant shrink-0" />
      </button>

      {open && (
        <div
          role="listbox"
          className="absolute left-0 top-[calc(100%+4px)] z-50 w-[280px] max-w-[calc(100vw-32px)] max-h-[320px] overflow-y-auto rounded-lg border border-outline-variant bg-surface-container-lowest p-xs ambient-shadow animate-fade-up"
        >
          {datasets.length === 0 && (
            <p className="px-sm py-md font-body-sm text-body-sm text-on-surface-variant">
              No projects yet. Upload a file to start one.
            </p>
          )}

          {datasets.map((d) => {
            const selected = d.id === activeId
            return (
              <button
                key={d.id}
                role="option"
                aria-selected={selected}
                onClick={() => {
                  setActiveId(d.id)
                  setOpen(false)
                }}
                className={`w-full flex items-center gap-sm px-sm py-sm rounded transition-colors text-left ${
                  selected ? 'bg-primary-fixed/50' : 'hover:bg-surface-container-low'
                }`}
              >
                <Icon
                  name={selected ? 'check' : 'table_view'}
                  size={15}
                  className={selected ? 'text-primary shrink-0' : 'text-outline shrink-0'}
                />
                <span className="min-w-0 flex-1">
                  <span className="block font-label-bold text-label-bold text-on-surface truncate">
                    {d.name}
                  </span>
                  <span className="block font-body-sm text-body-sm text-on-surface-variant">
                    {int(d.rows)} rows · {d.columns} columns
                  </span>
                </span>
              </button>
            )
          })}

          <Link
            to="/datasets"
            onClick={() => setOpen(false)}
            className="mt-xs flex items-center gap-sm px-sm py-sm rounded border-t border-outline-variant font-label-bold text-label-bold text-primary hover:bg-surface-container-low transition-colors"
          >
            <Icon name="upload" size={15} />
            Upload a new file
          </Link>
        </div>
      )}
    </div>
  )
}

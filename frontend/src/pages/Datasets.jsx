import { useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { PageCanvas, PageHeader } from '../components/AppShell'
import { Button, ErrorState, IconButton, IconTile, Panel, PanelHeader, Skeleton, StatusChip } from '../components/ui'
import Icon from '../components/Icon'
import Modal from '../components/Modal'
import { useToast } from '../components/Toast'
import { api } from '../lib/api'
import { useResource } from '../hooks/useResource'
import { useDatasets, usePageActions } from '../context/AppContext'
import { downloadCsv } from '../lib/csv'
import { int, whenLabel } from '../lib/format'

const PREVIEW_ROWS = 8

/** Drag-and-drop target. The API profiles the file and returns the profile. */
function Dropzone({ onUploaded, inputRef }) {
  const [dragging, setDragging] = useState(false)
  const [state, setState] = useState({ phase: 'idle' })
  const toast = useToast()

  const start = async (files) => {
    const file = files?.[0]
    if (!file) return

    setState({ phase: 'uploading', name: file.name })
    try {
      const result = await api.datasets.upload(file)
      setState({ phase: 'idle' })
      toast(`${file.name} uploaded — ${int(result.rowsImported)} rows profiled.`)
      onUploaded(result.dataset.id)
    } catch (cause) {
      setState({ phase: 'error', name: file.name, error: cause })
    }
  }

  if (state.phase === 'uploading') {
    return (
      <Panel className="p-md flex flex-col gap-sm">
        <div className="flex items-center gap-sm">
          <Icon name="upload_file" size={20} className="text-primary" />
          <span className="font-label-bold text-label-bold text-on-surface truncate">{state.name}</span>
        </div>
        <div
          className="h-1.5 w-full bg-surface-container rounded-full overflow-hidden"
          role="progressbar"
          aria-label={`Uploading ${state.name}`}
        >
          <div className="h-full w-1/2 bg-primary-container rounded-full animate-pulse" />
        </div>
        <p className="font-body-sm text-body-sm text-on-surface-variant">
          Reading rows and profiling columns.
        </p>
      </Panel>
    )
  }

  if (state.phase === 'error') {
    return (
      <Panel className="p-md flex flex-col gap-sm border-danger/40 bg-danger-container/20">
        <div className="flex items-center gap-sm">
          <Icon name="error" size={20} className="text-danger" />
          <span className="font-label-bold text-label-bold text-on-surface truncate">
            {state.error?.title ?? 'Upload failed'}
          </span>
        </div>
        <p className="font-body-main text-body-main text-on-surface-variant">
          {state.error?.detail || 'The upload did not complete.'}
        </p>
        <button
          onClick={() => setState({ phase: 'idle' })}
          className="self-start font-label-bold text-label-bold text-primary hover:text-surface-tint transition-colors"
        >
          Choose another file
        </button>
      </Panel>
    )
  }

  return (
    <div
      role="button"
      tabIndex={0}
      onClick={() => inputRef.current?.click()}
      onKeyDown={(e) => {
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault()
          inputRef.current?.click()
        }
      }}
      onDragEnter={(e) => {
        e.preventDefault()
        setDragging(true)
      }}
      onDragOver={(e) => e.preventDefault()}
      onDragLeave={() => setDragging(false)}
      onDrop={(e) => {
        e.preventDefault()
        setDragging(false)
        start(e.dataTransfer.files)
      }}
      className={`border-2 border-dashed border-outline-variant rounded-xl bg-surface-container-lowest/60 py-lg px-md flex flex-col items-center justify-center gap-sm cursor-pointer hover:bg-surface-container-low transition-colors ${
        dragging ? 'drag-active' : ''
      }`}
    >
      <span className="w-10 h-10 rounded-full bg-surface-container flex items-center justify-center text-on-surface-variant">
        <Icon name="cloud_upload" size={20} />
      </span>
      <p className="font-section-title text-section-title text-on-surface mt-sm">Drag &amp; drop files here</p>
      <p className="font-body-main text-body-main text-on-surface-variant">
        CSV, TSV or TXT
      </p>
      <input
        ref={inputRef}
        className="hidden"
        type="file"
        accept=".csv,.tsv,.txt"
        onChange={(e) => start(e.target.files)}
      />
    </div>
  )
}

/** The preview grid on the right: the first rows of the file, under its own headers. */
function RowPreview({ datasetId }) {
  const columns = useResource(
    () => (datasetId ? api.explorer.columns(datasetId) : Promise.resolve(null)),
    [datasetId]
  )
  const rows = useResource(
    () => (datasetId ? api.explorer.rows({ datasetId, page: 1, pageSize: PREVIEW_ROWS }) : Promise.resolve(null)),
    [datasetId]
  )

  if (rows.loading || columns.loading) {
    return (
      <div className="p-md space-y-sm">
        {Array.from({ length: 5 }).map((_, i) => (
          <Skeleton key={i} className="h-8 w-full" />
        ))}
      </div>
    )
  }

  const failure = columns.error ?? rows.error
  if (failure) {
    return (
      <div className="p-md">
        <p className="font-label-bold text-label-bold text-on-surface">{failure.title}</p>
        <p className="font-body-main text-body-main text-on-surface-variant mt-xs">
          {failure.detail || 'The rows of this file could not be read.'}
        </p>
      </div>
    )
  }

  const cols = columns.data?.columns ?? []
  const items = rows.data?.items ?? []

  return (
    <div className="overflow-x-auto">
      <table className="w-full text-left border-collapse" style={{ minWidth: `${Math.max(560, cols.length * 130)}px` }}>
        <thead>
          <tr className="border-y border-outline-variant bg-surface-container-low/60">
            {cols.map((c) => (
              <th
                key={c.key}
                scope="col"
                className={`font-label-bold text-stat-label uppercase text-on-surface-variant py-sm px-md whitespace-nowrap ${
                  c.align === 'right' ? 'text-right' : ''
                }`}
              >
                {c.label}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-outline-variant/50">
          {items.map((row) => (
            <tr key={row.id} className="hover:bg-surface-container-low/60 transition-colors">
              {cols.map((c, i) => (
                <td
                  key={c.key}
                  className={`py-sm px-md whitespace-nowrap max-w-[240px] truncate ${
                    c.numeric
                      ? 'text-right font-code text-code text-on-surface tabular-nums'
                      : 'font-body-main text-body-main text-on-surface'
                  }`}
                >
                  {row.cells[i] === '' ? '—' : row.cells[i]}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
      <div className="flex items-center justify-between gap-md px-md py-sm">
        <p className="font-body-sm text-body-sm text-on-surface-variant">
          First {items.length} of {int(rows.data?.total ?? 0)} rows, in file order
        </p>
        <Link
          to="/explorer"
          className="font-label-bold text-label-bold text-primary hover:text-surface-tint transition-colors"
        >
          Open in Data Explorer
        </Link>
      </div>
    </div>
  )
}

export default function Datasets() {
  const { datasets, activeId, setActiveId, reload, loading, error } = useDatasets()
  const navigate = useNavigate()
  const toast = useToast()
  const uploadInput = useRef(null)

  const [query, setQuery] = useState('')
  const [searchOpen, setSearchOpen] = useState(false)
  const [pendingDelete, setPendingDelete] = useState(null)
  const [deleting, setDeleting] = useState(false)

  const detail = useResource(
    () => (activeId ? api.datasets.get(activeId) : Promise.resolve(null)),
    [activeId]
  )

  usePageActions(
    () => ({
      filterLabel: 'Search the library',
      onFilter: () => setSearchOpen((v) => !v),
      exportLabel: 'Export the dataset library as CSV',
      onExport: datasets.length
        ? () =>
            downloadCsv(
              'dataset-library',
              [
                { label: 'Name', value: (d) => d.name },
                { label: 'Type', value: (d) => d.type },
                { label: 'Rows', value: (d) => d.rows },
                { label: 'Columns', value: (d) => d.columns },
                { label: 'Quality', value: (d) => d.quality },
                { label: 'Updated', value: (d) => d.updatedAt },
              ],
              datasets
            )
        : undefined,
    }),
    [datasets]
  )

  const visible = query
    ? datasets.filter((d) => d.name.toLowerCase().includes(query.toLowerCase()))
    : datasets

  const confirmDelete = async () => {
    setDeleting(true)
    try {
      await api.datasets.remove(pendingDelete.id)
      toast(`${pendingDelete.name} deleted.`)
      setPendingDelete(null)
      reload()
    } catch (cause) {
      // The API refuses to delete the file every other screen reads from.
      toast(cause.detail || cause.title, 'error')
      setPendingDelete(null)
    } finally {
      setDeleting(false)
    }
  }

  const dataset = detail.data?.dataset
  const columns = detail.data?.columns ?? []
  const missing = columns.reduce((sum, c) => sum + c.missing, 0)
  const cells = Math.max(1, (dataset?.rows ?? 0) * (dataset?.columns ?? 0))
  const missingPct = (missing / cells) * 100

  return (
    <PageCanvas>
      <PageHeader
        title="Datasets"
        action={
          <Button
            onClick={() => uploadInput.current?.click()} variant="primary" icon="upload">Upload a file</Button>
        }
      />

      <div className="grid grid-cols-12 gap-md items-start">
        <div className="col-span-12 lg:col-span-4 flex flex-col gap-md">
          <Dropzone
            inputRef={uploadInput}
            onUploaded={(id) => {
              reload()
              setActiveId(id)
            }}
          />

          <Panel className="flex flex-col">
            <div className="p-md border-b border-outline-variant">
              <PanelHeader
                title="Dataset Library"
                action={
                  <IconButton
                    onClick={() => setSearchOpen((v) => !v)}
                    aria-label="Search the library"
                    aria-expanded={searchOpen} icon="filter_list" />
                }
              />
              {searchOpen && (
                <input
                  autoFocus
                  type="search"
                  value={query}
                  onChange={(e) => setQuery(e.target.value)}
                  placeholder="Filter by name..."
                  aria-label="Filter the dataset library"
                  className="mt-md w-full h-[30px] rounded-lg border border-outline-variant bg-surface-container-lowest px-3 font-body-main text-body-main text-on-surface placeholder:text-outline focus:outline-none focus:border-primary-container transition-colors"
                />
              )}
            </div>

            <ul className="p-sm flex flex-col gap-xs max-h-[360px] overflow-y-auto">
              {loading && (
                <li className="p-sm space-y-sm">
                  {Array.from({ length: 3 }).map((_, i) => (
                    <Skeleton key={i} className="h-14 w-full rounded-lg" />
                  ))}
                </li>
              )}
              {error && (
                <li>
                  <ErrorState error={error} onRetry={reload} />
                </li>
              )}
              {!loading && visible.length === 0 && (
                <li className="p-md font-body-main text-body-main text-on-surface-variant">
                  {query ? `Nothing matched "${query}".` : 'No datasets yet. Upload a CSV to start.'}
                </li>
              )}
              {visible.map((d, i) => {
                const selected = d.id === activeId
                return (
                  <li
                    key={d.id}
                    className="group relative animate-fade-up"
                    style={{ animationDelay: `${Math.min(i, 8) * 35}ms` }}
                  >
                    <button
                      onClick={() => setActiveId(d.id)}
                      aria-current={selected}
                      className={`w-full text-left rounded-lg border p-md pr-11 transition-colors ${
                        selected
                          ? 'border-primary-container bg-primary-fixed/40'
                          : 'border-transparent hover:bg-surface-container-low'
                      }`}
                    >
                      <span className="flex items-center gap-sm">
                        <Icon name={d.icon} size={18} className={selected ? 'text-primary' : 'text-on-surface-variant'} />
                        <span className="font-label-bold text-[14px] text-on-surface truncate flex-1">{d.name}</span>
                        <StatusChip status={d.quality} />
                      </span>
                      <span className="block font-body-sm text-body-sm text-on-surface-variant mt-xs">
                        {int(d.rows)} rows · {d.columns} cols · {d.type}
                      </span>
                    </button>

                    {/* Deleting a file should not require selecting it first.
                        Always present on touch, where there is no hover to
                        reveal it with. */}
                    <IconButton
                      onClick={() => setPendingDelete(d)}
                      aria-label={`Delete ${d.name}`}
                      title={`Delete ${d.name}`} icon="delete" tone="danger" />
                  </li>
                )
              })}
            </ul>
          </Panel>
        </div>

        <Panel className="col-span-12 lg:col-span-8 flex flex-col min-h-[520px]">
          {detail.error ? (
            <ErrorState error={detail.error} onRetry={detail.reload} className="p-md" />
          ) : detail.loading || !dataset ? (
            <div className="p-md space-y-md">
              <Skeleton className="h-8 w-64" />
              <Skeleton className="h-20 w-full" />
              <Skeleton className="h-40 w-full" />
            </div>
          ) : (
            <>
              <div className="p-md flex flex-wrap items-center justify-between gap-md">
                <div className="flex items-center gap-md min-w-0">
                  <IconTile icon={dataset.icon} size={32} />
                  <div className="min-w-0">
                    <h3 className="font-page-title text-page-title text-on-surface truncate">
                      {dataset.name}
                    </h3>
                    <p className="font-body-main text-body-main text-on-surface-variant">
                      Updated {whenLabel(dataset.updatedAt)}
                    </p>
                  </div>
                </div>

                <div className="flex items-center gap-sm">
                  <Button
                    onClick={() => setPendingDelete(dataset)} variant="quiet" size="sm" icon="delete">Delete</Button>
                  <Button
                    onClick={() => navigate('/analytics')} variant="primary" size="sm" icon="auto_awesome">Analyze</Button>
                </div>
              </div>

              <dl className="grid grid-cols-1 sm:grid-cols-3 border-y border-outline-variant divide-y sm:divide-y-0 sm:divide-x divide-outline-variant">
                {[
                  ['Rows', int(dataset.rows), 'text-on-surface'],
                  ['Columns', String(dataset.columns), 'text-on-surface'],
                  [
                    'Missing values',
                    `${missingPct.toFixed(1)}%`,
                    missingPct > 1 ? 'text-danger' : 'text-on-surface',
                  ],
                ].map(([label, value, tone]) => (
                  <div key={label} className="p-md">
                    <dt className="font-label-bold text-stat-label uppercase text-on-surface-variant">{label}</dt>
                    <dd className={`font-kpi-value text-[17px] leading-6 tabular-nums mt-xs ${tone}`}>{value}</dd>
                  </div>
                ))}
              </dl>

              <RowPreview datasetId={dataset.id} />

              <div className="p-md border-t border-outline-variant mt-auto">
                <h4 className="font-label-bold text-label-bold text-on-surface">Column profile</h4>
                <ul className="mt-sm flex flex-wrap gap-sm">
                  {columns.map((c) => (
                    <li
                      key={c.name}
                      title={`${c.distinct} distinct · ${c.missing} missing`}
                      className="inline-flex items-center gap-sm rounded-lg border border-outline-variant px-sm py-[4px]"
                    >
                      <span className="font-code text-code text-on-surface">{c.name}</span>
                      <span className="font-body-sm text-body-sm text-on-surface-variant">{c.kind}</span>
                    </li>
                  ))}
                </ul>
              </div>
            </>
          )}
        </Panel>
      </div>

      <Modal
        open={Boolean(pendingDelete)}
        onClose={() => setPendingDelete(null)}
        title="Delete this dataset?"
        description={
          pendingDelete
            ? `${pendingDelete.name} and its ${int(pendingDelete.rows)} rows will be removed, along with the AI Assistant conversations about it and the reports written from it. This cannot be undone.`
            : ''
        }
        footer={
          <>
            <Button
              onClick={() => setPendingDelete(null)} variant="secondary">Keep dataset</Button>
            <Button
              onClick={confirmDelete}
              disabled={deleting} variant="danger">
              {deleting ? 'Deleting…' : 'Delete dataset'}
            </Button>
          </>
        }
      />
    </PageCanvas>
  )
}

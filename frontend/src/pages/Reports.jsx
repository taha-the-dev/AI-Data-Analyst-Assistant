import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { PageCanvas, PageHeader } from '../components/AppShell'
import { Button, ErrorState, IconTile, Panel, Skeleton, StatusChip, TableSkeleton } from '../components/ui'
import Icon from '../components/Icon'
import { RankedBars, TrendChart } from '../components/charts'
import { useToast } from '../components/Toast'
import { api } from '../lib/api'
import { useResource } from '../hooks/useResource'
import Modal from '../components/Modal'
import { useDatasets, usePageActions } from '../context/AppContext'
import { downloadCsv } from '../lib/csv'
import { compactMoney, int, whenLabel } from '../lib/format'

export function ReportsList() {
  const { data, error, loading, reload } = useResource(() => api.reports.list())
  const navigate = useNavigate()
  const toast = useToast()
  const { activeId, active } = useDatasets()

  // null when the dialog is closed; otherwise the title being written.
  const [draftTitle, setDraftTitle] = useState(null)
  const [creating, setCreating] = useState(false)
  const [pendingDelete, setPendingDelete] = useState(null)

  const reports = data ?? []

  const create = async () => {
    const title = draftTitle?.trim()
    setCreating(true)
    try {
      const report = await api.reports.create(title, activeId)
      setDraftTitle(null)
      await reload()
      toast(`"${report.title}" written from ${report.figures} live figures.`)
      navigate(`/reports/${report.id}`)
    } catch (cause) {
      toast(cause.detail || cause.title, 'error')
    } finally {
      setCreating(false)
    }
  }

  const remove = async () => {
    try {
      await api.reports.remove(pendingDelete.id)
      toast(`"${pendingDelete.title}" deleted.`)
      setPendingDelete(null)
      reload()
    } catch (cause) {
      toast(cause.detail || cause.title, 'error')
      setPendingDelete(null)
    }
  }

  usePageActions(
    () => ({
      exportLabel: 'Export the report index as CSV',
      onExport: reports.length
        ? () =>
            downloadCsv(
              'reports-directory',
              [
                { label: 'Title', value: (r) => r.title },
                { label: 'Dataset', value: (r) => r.dataset },
                { label: 'Figures', value: (r) => r.figures },
                { label: 'Status', value: (r) => r.status },
                { label: 'Created', value: (r) => r.createdAt },
              ],
              reports
            )
        : undefined,
    }),
    [data]
  )

  return (
    <PageCanvas>
      <PageHeader
        title="Reports"
        action={
          <Button
            onClick={() => setDraftTitle(active ? `${active.name} review` : 'Untitled report')} variant="primary" icon="add">Create Report</Button>
        }
      />

      <Panel className="overflow-hidden">
        {error ? (
          <ErrorState error={error} onRetry={reload} className="p-md" />
        ) : loading ? (
          <TableSkeleton rows={3} />
        ) : reports.length === 0 ? (
          <div className="p-lg text-center">
            <p className="font-label-bold text-label-bold text-on-surface">No reports yet</p>
            <p className="font-body-main text-body-main text-on-surface-variant mt-xs max-w-md mx-auto">
              A report is a title and a source file; its body is composed from live figures every time it is
              opened, so it can never drift from the data. Create one from {active?.name ?? 'the active file'}.
            </p>
            <Button
              onClick={() => setDraftTitle(active ? `${active.name} review` : 'Untitled report')} variant="primary" size="sm" icon="add">Create the first report</Button>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[720px] text-left border-collapse">
              <thead>
                <tr className="border-b border-outline-variant">
                  <th scope="col" className="font-label-bold text-stat-label uppercase text-on-surface-variant py-sm px-md w-[40%]">
                    Report title
                  </th>
                  <th scope="col" className="font-label-bold text-stat-label uppercase text-on-surface-variant py-sm px-sm">
                    Updated date
                  </th>
                  <th scope="col" className="font-label-bold text-stat-label uppercase text-on-surface-variant py-sm px-sm">
                    Source file
                  </th>
                  <th scope="col" className="font-label-bold text-stat-label uppercase text-on-surface-variant py-sm px-sm">
                    Status
                  </th>
                  <th scope="col" className="font-label-bold text-stat-label uppercase text-on-surface-variant py-sm px-md text-right">
                    Actions
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y divide-outline-variant/50">
                {reports.map((r, i) => (
                  <tr
                    key={r.id}
                    className="group hover:bg-surface-container-low/60 transition-colors animate-fade-up"
                    style={{ animationDelay: `${Math.min(i, 8) * 35}ms` }}
                  >
                    <td className="py-sm px-md">
                      <div className="flex items-center gap-md">
                        <IconTile icon="insert_chart" size={30} />
                        <div className="min-w-0">
                          <Link
                            to={`/reports/${r.id}`}
                            className="font-label-bold text-label-bold text-on-surface hover:text-primary transition-colors"
                          >
                            {r.title}
                          </Link>
                          {r.status === 'Source deleted' ? (
                            <p className="font-body-sm text-body-sm text-danger">
                              {r.dataset} was deleted — nothing left to compose from
                            </p>
                          ) : (
                            <p className="font-body-sm text-body-sm text-primary">
                              Composed from {r.figures} live {r.figures === 1 ? 'figure' : 'figures'}
                            </p>
                          )}
                        </div>
                      </div>
                    </td>
                    <td className="py-sm px-sm font-body-main text-body-main text-on-surface-variant whitespace-nowrap">
                      {whenLabel(r.createdAt)}
                    </td>
                    <td className="py-sm px-sm font-code text-code text-on-surface-variant">{r.dataset}</td>
                    <td className="py-sm px-sm">
                      <StatusChip status={r.status} />
                    </td>
                    <td className="py-sm px-md">
                      <div className="flex items-center justify-end gap-xs">
                        <Link
                          to={`/reports/${r.id}`}
                          aria-label={`Open ${r.title}`}
                          title="Open"
                          className="inline-flex w-9 h-9 items-center justify-center rounded-lg text-on-surface-variant hover:bg-surface-container transition-colors"
                        >
                          <Icon name="open_in_new" size={15} />
                        </Link>
                        <button
                          onClick={() => setPendingDelete(r)}
                          aria-label={`Delete ${r.title}`}
                          title="Delete"
                          className="inline-flex w-9 h-9 items-center justify-center rounded-lg text-on-surface-variant hover:bg-danger-container hover:text-danger transition-colors"
                        >
                          <Icon name="delete" size={15} />
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Panel>

      <Modal
        open={draftTitle !== null}
        onClose={() => setDraftTitle(null)}
        title="Create a report"
        description={`Composed from ${active?.name ?? 'the active file'} when it is opened — the figures are computed then, not stored now.`}
        footer={
          <>
            <Button
              onClick={() => setDraftTitle(null)} variant="secondary">Cancel</Button>
            <Button
              onClick={create}
              disabled={creating} variant="primary">
              {creating ? 'Writing…' : 'Create report'}
            </Button>
          </>
        }
      >
        <label className="sr-only" htmlFor="report-title">
          Report title
        </label>
        <input
          id="report-title"
          autoFocus
          value={draftTitle ?? ''}
          onChange={(e) => setDraftTitle(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && create()}
          placeholder="Q3 revenue review"
          className="w-full h-[32px] rounded-lg border border-outline-variant bg-surface-container-lowest px-3 font-body-main text-body-main text-on-surface placeholder:text-outline focus:outline-none focus:border-primary-container transition-colors"
        />
      </Modal>

      <Modal
        open={Boolean(pendingDelete)}
        onClose={() => setPendingDelete(null)}
        title="Delete this report?"
        description={pendingDelete ? `"${pendingDelete.title}" will be removed. The data it described is untouched.` : ''}
        footer={
          <>
            <Button
              onClick={() => setPendingDelete(null)} variant="secondary">Keep it</Button>
            <Button
              onClick={remove} variant="danger">
              Delete report
            </Button>
          </>
        }
      />
    </PageCanvas>
  )
}

/** Renders the fetched report as Markdown so the download is the real thing. */
function toMarkdown({ report, meta, sections }) {
  const lines = [`# ${report.title}`, '', meta, '']
  for (const section of sections) {
    lines.push(`## ${section.heading}`, '')
    section.paragraphs.forEach((p) => lines.push(p, ''))
    if (section.figure) {
      lines.push(`**Figure ${section.figure.number}.** ${section.figure.caption}`, '')
      lines.push('| Label | Value |', '| --- | --- |')
      section.figure.data.forEach((f) => lines.push(`| ${f.label} | ${f.value} |`))
      lines.push('')
    }
  }
  return lines.join('\n')
}

export function ReportReader() {
  const { id } = useParams()
  const toast = useToast()
  const { data, error, loading, reload } = useResource(() => api.reports.get(id), [id])

  usePageActions(
    () => ({
      exportLabel: 'Download this report as Markdown',
      onExport: data
        ? () => {
            const blob = new Blob([toMarkdown(data)], { type: 'text/markdown;charset=utf-8' })
            const url = URL.createObjectURL(blob)
            const link = document.createElement('a')
            link.href = url
            link.download = `${data.report.title.replace(/\s+/g, '-').toLowerCase()}.md`
            document.body.appendChild(link)
            link.click()
            link.remove()
            setTimeout(() => URL.revokeObjectURL(url), 0)
            toast('Report downloaded as Markdown.')
          }
        : undefined,
    }),
    [data]
  )

  if (error) {
    return (
      <PageCanvas>
        <Link
          to="/reports"
          className="inline-flex items-center gap-xs font-label-bold text-label-bold text-on-surface-variant hover:text-primary transition-colors w-fit"
        >
          <Icon name="arrow_back" size={16} />
          All reports
        </Link>
        {/* A deleted source file will not come back on a retry, so the button
            that suggests it would is left off. */}
        <ErrorState
          error={error}
          onRetry={error.status === 0 || error.status >= 500 ? reload : undefined}
        />
      </PageCanvas>
    )
  }

  if (loading || !data) {
    return (
      <PageCanvas>
        <Skeleton className="h-8 w-64" />
        <Skeleton className="h-64 w-full" />
      </PageCanvas>
    )
  }

  const { report, meta, sections } = data

  return (
    <PageCanvas>
      <Link
        to="/reports"
        className="inline-flex items-center gap-xs font-label-bold text-label-bold text-on-surface-variant hover:text-primary transition-colors w-fit"
      >
        <Icon name="arrow_back" size={16} />
        All reports
      </Link>

      <PageHeader
        title={report.title}
        description={meta}
        action={
          <Button
            onClick={() => {
              navigator.clipboard
                ?.writeText(window.location.href)
                .then(() => toast('Link copied to the clipboard.'))
                .catch(() => toast('Could not access the clipboard.', 'error'))
            }} variant="secondary" icon="link">Copy link</Button>
        }
      />

      <Panel className="p-md md:p-lg max-w-3xl w-full">
        <article className="flex flex-col gap-lg">
          {sections.map((section) => (
            <section key={section.heading} className="flex flex-col gap-sm">
              <h3 className="font-section-title text-[15px] leading-5 text-on-surface">{section.heading}</h3>
              {section.paragraphs.map((p) => (
                <p key={p} className="font-body-main text-body-main text-on-surface-variant">
                  {p}
                </p>
              ))}

              {section.figure && (
                <figure className="mt-sm border border-outline-variant rounded-xl bg-background p-md">
                  {section.figure.kind === 'trend' ? (
                    <TrendChart
                      points={section.figure.data}
                      format={compactMoney}
                      className="h-[180px]"
                      gradientId={`fig-${section.figure.number}`}
                      ariaLabel={section.figure.caption}
                    />
                  ) : (
                    <RankedBars items={section.figure.data} format={compactMoney} />
                  )}
                  <figcaption className="font-body-sm text-body-sm text-on-surface-variant mt-md pt-sm border-t border-outline-variant/50">
                    <span className="font-label-bold text-on-surface">Figure {section.figure.number}.</span>{' '}
                    {section.figure.caption} — computed from{' '}
                    <span className="font-code tabular-nums">{int(section.figure.rowsScanned)}</span> rows.
                  </figcaption>
                </figure>
              )}
            </section>
          ))}
        </article>
      </Panel>
    </PageCanvas>
  )
}

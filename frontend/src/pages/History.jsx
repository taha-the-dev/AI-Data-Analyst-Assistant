import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageCanvas, PageHeader } from '../components/AppShell'
import { Button, ErrorState, IconButton, IconTile, Pagination, Panel, Skeleton, StatusChip } from '../components/ui'
import Icon from '../components/Icon'
import Modal from '../components/Modal'
import { useToast } from '../components/Toast'
import { api } from '../lib/api'
import { useResource } from '../hooks/useResource'
import { useDatasets, usePageActions } from '../context/AppContext'
import { downloadCsv } from '../lib/csv'
import { whenLabel } from '../lib/format'

const PER_PAGE = 6

/**
 * The stored conversations about the file selected in the top bar, most recent
 * first. These are the analyses the assistant ran — each one still holds the
 * spec and figures behind its answers, so opening one restores the whole working.
 */
/** The line under a conversation's title: its summary, if it still has a true one. */
function summaryOf(session) {
  const stale = session.messageCount > 0 && session.subtitle === 'No questions yet'
  const summary = stale ? '' : session.subtitle
  return summary ? `${summary} · ${whenLabel(session.updatedAt)}` : whenLabel(session.updatedAt)
}

export default function History() {
  const navigate = useNavigate()
  const toast = useToast()
  const [page, setPage] = useState(1)
  const [query, setQuery] = useState('')
  const [searchOpen, setSearchOpen] = useState(false)
  const [pendingDelete, setPendingDelete] = useState(null)

  const { activeId, active, loading: filesLoading } = useDatasets()

  // Only the selected file's conversations; switching file switches the list.
  const { data, error, loading, reload } = useResource(
    () => (activeId ? api.chat.sessions(activeId) : Promise.resolve([])),
    [activeId]
  )
  const sessions = data ?? []

  // A new file starts on its first page.
  useEffect(() => setPage(1), [activeId])

  const visible = query
    ? sessions.filter((s) => `${s.title} ${s.subtitle}`.toLowerCase().includes(query.toLowerCase()))
    : sessions

  usePageActions(
    () => ({
      filterLabel: 'Search these analyses',
      onFilter: () => setSearchOpen((v) => !v),
      exportLabel: 'Export this history as CSV',
      onExport: sessions.length
        ? () =>
            downloadCsv(
              `${active?.name ?? 'analysis'}-history`,
              [
                { label: 'Title', value: (s) => s.title },
                { label: 'Summary', value: (s) => s.subtitle },
                { label: 'Turns', value: (s) => s.messageCount },
                { label: 'Updated', value: (s) => s.updatedAt },
              ],
              sessions
            )
        : undefined,
    }),
    [data, active?.name]
  )

  const pageCount = Math.max(1, Math.ceil(visible.length / PER_PAGE))
  const shown = visible.slice((page - 1) * PER_PAGE, page * PER_PAGE)

  const remove = async () => {
    try {
      await api.chat.remove(pendingDelete.id)
      toast(`${pendingDelete.title} deleted.`)
      setPendingDelete(null)
      reload()
    } catch (cause) {
      toast(cause.detail || cause.title, 'error')
      setPendingDelete(null)
    }
  }

  return (
    <PageCanvas>
      {/* No Search button here: the top bar's Filter already opens this screen's
          filter, and two controls for one job is two things to learn. */}
      <PageHeader title="History" />

      {searchOpen && (
        <input
          autoFocus
          type="search"
          value={query}
          onChange={(e) => {
            setQuery(e.target.value)
            setPage(1)
          }}
          placeholder="Filter by title..."
          aria-label="Filter analyses"
          className="w-full sm:w-[360px] h-[30px] rounded-lg border border-outline-variant bg-surface-container-lowest px-3 font-body-main text-body-main text-on-surface placeholder:text-outline focus:outline-none focus:border-primary-container transition-colors"
        />
      )}

      <Panel className="overflow-hidden">
        {error ? (
          <ErrorState error={error} onRetry={reload} className="p-md" />
        ) : loading || filesLoading ? (
          <div className="p-md space-y-md">
            {Array.from({ length: 4 }).map((_, i) => (
              <Skeleton key={i} className="h-16 w-full rounded-lg" />
            ))}
          </div>
        ) : visible.length === 0 ? (
          <div className="p-lg text-center">
            <p className="font-label-bold text-label-bold text-on-surface">
              {query
                ? 'Nothing matched that search'
                : active
                  ? `No analyses of ${active.name} yet`
                  : 'No file selected'}
            </p>
            <p className="font-body-main text-body-main text-on-surface-variant mt-xs">
              {query
                ? 'Try a shorter term, or clear the filter.'
                : active
                  ? 'Ask the assistant a question about this file and the conversation is stored here.'
                  : 'Upload a file on the Datasets page, then ask the assistant about it.'}
            </p>
          </div>
        ) : (
          <ul className="divide-y divide-outline-variant/50">
            {shown.map((s, i) => (
              <li
                key={s.id}
                className="flex flex-wrap items-center gap-md p-lg hover:bg-surface-container-low/60 transition-colors animate-fade-up"
                style={{ animationDelay: `${Math.min(i, 8) * 40}ms` }}
              >
                <IconTile icon="query_stats" size={32} />
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-sm">
                    <button
                      onClick={() => navigate(`/analyst?session=${s.id}`)}
                      className="font-section-title text-section-title text-on-surface hover:text-primary transition-colors truncate"
                    >
                      {s.title}
                    </button>
                    <StatusChip status={`${s.messageCount} turns`} />
                  </div>
                  <p className="font-body-main text-body-main text-on-surface-variant truncate mt-xs">
                    {/* The server stamps "No questions yet" when a conversation is
                        created and replaces it only when one is named, so a row with
                        eight turns would otherwise claim nothing had been asked. */}
                    {summaryOf(s)}
                  </p>
                </div>

                <div className="flex items-center gap-xs">
                  <IconButton
                    onClick={() => navigate(`/analyst?session=${s.id}`)}
                    aria-label={`Open ${s.title}`} icon="open_in_new" />
                  <IconButton
                    onClick={() => setPendingDelete(s)}
                    aria-label={`Delete ${s.title}`} icon="delete" tone="danger" />
                </div>
              </li>
            ))}
          </ul>
        )}
      </Panel>

      {visible.length > 0 && (
        <Pagination
          page={page}
          pageCount={pageCount}
          onPage={(p) => setPage(Math.min(pageCount, Math.max(1, p)))}
          summary={`Showing ${(page - 1) * PER_PAGE + 1}-${Math.min(
            page * PER_PAGE,
            visible.length
          )} of ${visible.length} analyses of ${active?.name ?? 'this file'}`}
        />
      )}

      <Modal
        open={Boolean(pendingDelete)}
        onClose={() => setPendingDelete(null)}
        title="Delete this analysis?"
        description={
          pendingDelete
            ? `${pendingDelete.title} and its ${pendingDelete.messageCount} turns will be removed. This cannot be undone.`
            : ''
        }
        footer={
          <>
            <Button
              onClick={() => setPendingDelete(null)} variant="secondary">Keep it</Button>
            <Button
              onClick={remove} variant="danger">
              Delete analysis
            </Button>
          </>
        }
      />
    </PageCanvas>
  )
}

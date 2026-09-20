import { useEffect, useRef, useState } from 'react'
import { useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import { FixedCanvas } from '../components/AppShell'
import Icon from '../components/Icon'
import { Button, ErrorState, IconButton, Skeleton, StatusChip } from '../components/ui'
import Modal from '../components/Modal'
import { useToast } from '../components/Toast'
import Figure from '../components/Figure'
import { api } from '../lib/api'
import { useResource } from '../hooks/useResource'
import { useDatasets } from '../context/AppContext'
import { downloadCsv } from '../lib/csv'
import { bucketLabel, clock, int, unitValue } from '../lib/format'

/** One collapsible block in the analysis panel. */
function Section({ title, children, defaultOpen = true }) {
  const [open, setOpen] = useState(defaultOpen)
  return (
    <section className="border border-outline-variant rounded-xl overflow-hidden">
      <button
        onClick={() => setOpen((v) => !v)}
        aria-expanded={open}
        className="w-full flex items-center justify-between gap-sm bg-surface-container-low px-md py-sm hover:bg-surface-container transition-colors"
      >
        <span className="font-label-bold text-stat-label uppercase text-on-surface-variant">{title}</span>
        <Icon name={open ? 'expand_less' : 'expand_more'} size={18} className="text-on-surface-variant" />
      </button>
      {open && <div className="p-md">{children}</div>}
    </section>
  )
}

/** Right-hand panel: the figures behind the last answer, and a chart of them. */
function AnalysisPanel({ answer }) {
  if (!answer?.figures?.length) {
    return (
      <div className="flex flex-col items-center justify-center text-center h-full gap-sm px-lg">
        <span className="w-9 h-9 rounded-full bg-surface-container text-on-surface-variant flex items-center justify-center">
          <Icon name="bar_chart" size={18} />
        </span>
        <p className="font-label-bold text-label-bold text-on-surface">No analysis yet</p>
        <p className="font-body-main text-body-main text-on-surface-variant">
          Ask a question and the figures and chart behind the answer appear here.
        </p>
      </div>
    )
  }

  const top = answer.figures.slice(0, 10)
  // The API says how the figures are written; turns stored before it did are plain numbers.
  const unit = answer.unit ?? { prefix: '', suffix: '', decimals: 0 }
  const format = (v) => unitValue(v, unit, { compact: false })
  // A share of the whole only means something when the parts add up.
  const additive = ['sum', 'count'].includes(answer.spec?.aggregate)
  const total = additive ? answer.figures.reduce((sum, f) => sum + f.value, 0) : 0

  return (
    <div className="flex flex-col gap-md">
      <Section title={`Results (top ${top.length})`}>
        <table className="w-full text-left border-collapse">
          <thead>
            <tr className="border-b border-outline-variant">
              <th scope="col" className="font-label-bold text-stat-label uppercase text-on-surface-variant pb-sm">
                {answer.spec?.groupBy ?? 'Group'}
              </th>
              <th scope="col" className="font-label-bold text-stat-label uppercase text-on-surface-variant pb-sm text-right">
                Value
              </th>
              <th scope="col" className="font-label-bold text-stat-label uppercase text-on-surface-variant pb-sm text-right">
                Share
              </th>
            </tr>
          </thead>
          <tbody className="divide-y divide-outline-variant/50">
            {top.map((f) => (
              <tr key={f.label}>
                <td className="py-sm font-body-main text-body-main text-on-surface truncate max-w-[160px]">
                  {bucketLabel(f.label)}
                </td>
                <td className="py-sm text-right font-code text-code text-on-surface tabular-nums">
                  {format(f.value)}
                </td>
                {total > 0 && (
                  <td className="py-sm text-right font-code text-code text-on-surface-variant tabular-nums">
                    {`${((f.value / total) * 100).toFixed(1)}%`}
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
        {answer.figures.length > top.length && (
          <p className="font-body-sm text-body-sm text-on-surface-variant pt-sm">
            … ({answer.figures.length - top.length} more rows)
          </p>
        )}
      </Section>

      <Section title="Visualization">
        <Figure
          kind={answer.spec?.intent === 'trend' ? 'line' : answer.spec?.chart === 'donut' ? 'donut' : 'bars'}
          figures={answer.spec?.intent === 'trend' ? answer.figures : top}
          unit={unit}
          height={200}
          id={`answer-${answer.id}`}
          ariaLabel={answer.title ?? 'Answer'}
        />
      </Section>
    </div>
  )
}

export default function AiAnalyst() {
  const { activeId, active, datasets, setActiveId, loading: filesLoading } = useDatasets()
  const location = useLocation()
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const toast = useToast()

  const [activeSession, setActiveSession] = useState(() => {
    const id = Number(params.get('session'))
    return Number.isFinite(id) && id > 0 ? id : null
  })
  const [messages, setMessages] = useState([])
  const [input, setInput] = useState('')
  const [streamingId, setStreamingId] = useState(null)
  // Mirrors streamingId for readers that run inside the same commit.
  const streamingRef = useRef(null)
  const setStreaming = (id) => {
    streamingRef.current = id
    setStreamingId(id)
  }
  // null when the save dialog is closed; otherwise the name being edited.
  const [saveName, setSaveName] = useState(null)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [saving, setSaving] = useState(false)
  const scrollRef = useRef(null)
  const sourceRef = useRef(null)
  const askRef = useRef(null)

  const sessions = useResource(() => api.chat.sessions(), [])

  // A conversation belongs to the file it is about, so the picker offers the
  // conversations of the file selected in the top bar. One opened from History
  // that predates files being tied to conversations is shown as well.
  const allSessions = sessions.data ?? []
  const current = allSessions.find((s) => s.id === activeSession)
  const forFile = allSessions.filter((s) => s.datasetId === activeId)
  const pickable = current && !forFile.includes(current) ? [current, ...forFile] : forFile

  const openSession = (id) => {
    setActiveSession(id)
    setParams(id === null ? {} : { session: String(id) })
  }

  // Arriving on a conversation about another file — a link from History —
  // selects that file, so the answers and the file on screen agree. Once only:
  // after that, choosing a file chooses its conversations.
  const arrived = useRef(false)
  useEffect(() => {
    if (arrived.current || !sessions.data || (activeId === null && filesLoading)) return
    arrived.current = true
    const target = current?.datasetId
    if (target && target !== activeId && datasets.some((d) => d.id === target)) setActiveId(target)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sessions.data, activeId])

  // Choosing another file in the top bar moves to that file's latest
  // conversation. An answer still streaming is stored by the API regardless.
  const previousFile = useRef(activeId)
  useEffect(() => {
    const previous = previousFile.current
    previousFile.current = activeId
    if (previous === null || previous === activeId || !arrived.current) return
    if (current?.datasetId === activeId) return

    sourceRef.current?.close()
    sourceRef.current = null
    setStreaming(null)
    setMessages([])
    openSession(forFile[0]?.id ?? null)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeId])

  // Land on the file's most recent conversation once the list arrives. When
  // there is none, the screen stays usable and `ask` opens one on the first
  // question, so a visit that asks nothing leaves nothing behind in History.
  useEffect(() => {
    if (activeSession === null && arrived.current && forFile.length) openSession(forFile[0].id)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sessions.data, activeSession, activeId])

  const history = useResource(
    () => (activeSession === null ? Promise.resolve([]) : api.chat.messages(activeSession)),
    [activeSession]
  )

  useEffect(() => {
    // Asking the first question of a visit creates the conversation, which makes
    // this resource re-run and answer with an empty transcript. Adopting that
    // would erase the question the reader just asked, so a live stream wins.
    if (history.data && streamingRef.current === null) setMessages(history.data)
  }, [history.data])

  useEffect(() => {
    scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight, behavior: 'smooth' })
  }, [messages])

  // Close any open stream when the screen goes away.
  useEffect(() => () => sourceRef.current?.close(), [])

  const ask = async (question) => {
    const text = question.trim()
    if (!text || streamingId) return

    // First question of a fresh visit: there is nothing to append to yet.
    let sessionId = activeSession
    if (sessionId === null) {
      try {
        const created = await api.chat.create('New session', activeId)
        sessionId = created.id
        openSession(created.id)
        sessions.reload()
      } catch (cause) {
        toast.show(cause?.title ?? 'Could not start a conversation', 'error')
        return
      }
    }

    const now = new Date().toISOString()
    const replyId = `pending-${Date.now()}`

    setMessages((m) => [
      ...m,
      { id: `user-${Date.now()}`, role: 'user', content: text, createdAt: now },
      { id: replyId, role: 'assistant', content: '', createdAt: now, figures: [] },
    ])
    setInput('')
    setStreaming(replyId)

    // The API sends the plan and the computed figures before the prose, so the
    // analysis panel fills in before the sentence finishes.
    const source = new EventSource(api.chat.streamUrl(sessionId, text, activeId))
    sourceRef.current = source

    const patch = (fields) =>
      setMessages((m) => m.map((msg) => (msg.id === replyId ? { ...msg, ...fields } : msg)))

    source.addEventListener('plan', (e) => {
      const { spec, planner } = JSON.parse(e.data)
      patch({ spec, planner, title: spec.title, chart: spec.chart })
    })

    source.addEventListener('figures', (e) => {
      const { figures, unit, chart, title } = JSON.parse(e.data)
      patch({ figures, unit, chart, title })
    })

    source.addEventListener('token', (e) => {
      const { text: token } = JSON.parse(e.data)
      setMessages((m) =>
        m.map((msg) => (msg.id === replyId ? { ...msg, content: msg.content + token } : msg))
      )
    })

    source.addEventListener('done', () => {
      source.close()
      sourceRef.current = null
      setStreaming(null)
      sessions.reload()
    })

    // A refusal before streaming starts (no file, file uploaded before copies
    // were kept) arrives as a plain response, which EventSource reports as an
    // error; the message says what to do rather than blaming a restart.
    source.onerror = () => {
      source.close()
      sourceRef.current = null
      setStreaming(null)
      patch({
        content:
          'No answer came back. If this file was uploaded before files were kept for analysis, upload it again; ' +
          'otherwise the API may have restarted — ask again to retry.',
      })
    }
  }

  askRef.current = ask

  // A question handed over from another screen is asked once, on arrival.
  const handoff = location.state?.question
  useEffect(() => {
    if (!handoff || activeSession === null) return
    askRef.current(handoff)
    navigate(location.pathname, { replace: true, state: null })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [handoff, activeSession])

  const stop = () => {
    sourceRef.current?.close()
    sourceRef.current = null
    setStreaming(null)
    setMessages((m) =>
      m.map((msg) => (msg.id === streamingId ? { ...msg, content: `${msg.content} …` } : msg))
    )
  }

  const newSession = async () => {
    sourceRef.current?.close()
    setStreaming(null)
    const created = await api.chat.create('New session', activeId)
    await sessions.reload()
    openSession(created.id)
    setMessages([])
  }

  const currentTitle = current?.title ?? 'New session'

  /**
   * Saving a conversation is naming it. Every turn was stored as it happened,
   * so this is about being able to find it again in History — not about
   * flushing anything to the server.
   */
  const saveSession = async () => {
    const title = saveName?.trim()
    if (!title || activeSession === null) return

    setSaving(true)
    try {
      // The first question doubles as the subtitle, so the History list says
      // what the conversation was about without opening it.
      const firstQuestion = messages.find((m) => m.role === 'user')?.content
      await api.chat.save(activeSession, title, firstQuestion)
      await sessions.reload()
      toast(`Saved as "${title}".`)
      setSaveName(null)
    } catch (cause) {
      toast(cause.detail || cause.title, 'error')
    } finally {
      setSaving(false)
    }
  }

  const deleteSession = async () => {
    if (activeSession === null) return

    try {
      sourceRef.current?.close()
      setStreaming(null)
      await api.chat.remove(activeSession)

      const remaining = forFile.filter((s) => s.id !== activeSession)
      await sessions.reload()
      setConfirmDelete(false)
      setMessages([])

      // Land on whatever is left for this file rather than on an id that no longer exists.
      openSession(remaining[0]?.id ?? null)

      toast('Conversation deleted.')
    } catch (cause) {
      toast(cause.detail || cause.title, 'error')
      setConfirmDelete(false)
    }
  }

  const answers = messages.filter((m) => m.role === 'assistant' && m.figures?.length)
  const latest = answers[answers.length - 1]

  const exportFigures = () =>
    latest &&
    downloadCsv(
      `${latest.spec?.title ?? latest.title ?? 'analysis'}`.replace(/\s+/g, '-').toLowerCase(),
      [
        { label: latest.spec?.groupBy ?? 'Group', value: (f) => bucketLabel(f.label) },
        { label: latest.spec?.title ?? latest.title ?? 'Value', value: (f) => f.value },
      ],
      latest.figures
    )

  const busy = Boolean(streamingId)

  return (
    <FixedCanvas className="bg-background">
      <div className="px-md md:px-lg lg:px-xl pt-lg pb-md shrink-0">
        <h2 className="font-page-title text-page-title font-semibold text-on-surface tracking-[-0.02em]">
          AI Assistant
        </h2>
        <div className="flex flex-wrap items-center gap-md mt-xs">
          <p className="font-body-main text-body-main text-on-surface-variant">
            Every answer carries the figures behind it.
          </p>
          <div className="flex items-center gap-sm ml-auto">
            {/* A menu of one conversation is a menu of the screen you are already
                on, so below two this names the conversation instead. */}
            {pickable.length > 1 ? (
              <>
              <label className="sr-only" htmlFor="session-picker">
                Conversation
              </label>
              <select
                id="session-picker"
                value={activeSession ?? ''}
                onChange={(e) => openSession(e.target.value ? Number(e.target.value) : null)}
                className="h-[30px] rounded-lg border border-outline-variant bg-surface-container-lowest px-3 font-body-main text-body-main text-on-surface focus:outline-none focus:border-primary-container"
              >
                {activeSession === null && <option value="">New conversation</option>}
                {pickable.map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.title}
                  </option>
                ))}
              </select>
              </>
            ) : (
              activeSession !== null && (
                <span className="font-body-main text-body-main text-on-surface-variant truncate max-w-[240px]">
                  {currentTitle}
                </span>
              )
            )}
            <Button
              onClick={() => setSaveName(currentTitle)}
              disabled={activeSession === null}
              title="Save this conversation under a name" variant="secondary" size="sm" icon="bookmark">Save</Button>
            <Button
              onClick={() => setConfirmDelete(true)}
              disabled={activeSession === null}
              title="Delete this conversation" variant="quiet" size="sm" icon="delete">Delete</Button>
            <Button
              onClick={newSession} variant="primary" size="sm" icon="add">
              New
            </Button>
          </div>
        </div>
      </div>

      <div className="flex-1 flex min-h-0 border-t border-outline-variant">
        {/* Conversation */}
        <div className="flex-1 flex flex-col min-w-0">
          <div ref={scrollRef} className="flex-1 overflow-y-auto px-md md:px-lg lg:px-xl py-lg space-y-lg">
            {history.error ? (
              <ErrorState error={history.error} onRetry={history.reload} />
            ) : history.loading ? (
              <div className="space-y-md">
                <Skeleton className="h-16 w-2/3 ml-auto rounded-2xl" />
                <Skeleton className="h-32 w-4/5 rounded-2xl" />
              </div>
            ) : (
              <>
                {/* Centred on the icon: one line of text is shorter than the icon,
                    and the file name keeps the sentence's size so it sits on its baseline. */}
                <div className="flex items-center gap-sm">
                  <span className="w-8 h-8 rounded-lg bg-primary-container text-white flex items-center justify-center shrink-0">
                    <Icon name="smart_toy" size={15} />
                  </span>
                  <p className="font-body-main text-body-main text-on-surface max-w-[80%]">
                    Ready to analyze{' '}
                    <span className="font-medium text-primary">{active?.name ?? 'your file'}</span>
                    {active ? ` (${int(active.rows)} rows)` : ''}. What would you like to know?
                  </p>
                </div>

                {messages.map((m) =>
                  m.role === 'user' ? (
                    <div key={m.id} className="flex flex-col items-end animate-fade-up">
                      <div className="max-w-[80%] rounded-2xl rounded-tr-sm bg-surface-container px-md py-sm">
                        <p className="font-body-main text-body-main text-on-surface">{m.content}</p>
                      </div>
                      <span className="font-body-sm text-[12px] text-on-surface-variant mt-1 mr-1">
                        {clock(m.createdAt)}
                      </span>
                    </div>
                  ) : (
                    <div key={m.id} className="flex items-start gap-sm animate-fade-up">
                      <span className="w-8 h-8 rounded-lg bg-primary-container text-white flex items-center justify-center shrink-0">
                        <Icon name="smart_toy" size={15} />
                      </span>
                      <div className="max-w-[80%]">
                        <div className="rounded-2xl rounded-tl-sm border border-primary-fixed-dim bg-surface-container-lowest px-md py-sm">
                          <p className="font-body-main text-body-main text-on-surface">
                            {m.content}
                            {m.id === streamingId && (
                              <span className="inline-block w-[2px] h-[1em] align-[-2px] ml-0.5 bg-primary-container animate-blink" />
                            )}
                          </p>
                        </div>
                      </div>
                    </div>
                  )
                )}
              </>
            )}
          </div>

          <div className="shrink-0 px-md md:px-lg lg:px-xl pb-lg pt-sm bg-background">
            <form
              onSubmit={(e) => {
                e.preventDefault()
                ask(input)
              }}
              className="flex items-end gap-sm rounded-xl border border-outline-variant bg-surface-container-lowest p-sm focus-within:border-primary-container transition-colors"
            >
              <span className="w-8 h-8 flex items-center justify-center text-outline shrink-0">
                <Icon name="attach_file" size={16} />
              </span>
              <label className="sr-only" htmlFor="composer">
                Ask anything about your dataset
              </label>
              <textarea
                id="composer"
                rows={1}
                value={input}
                onChange={(e) => setInput(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter' && !e.shiftKey) {
                    e.preventDefault()
                    ask(input)
                  }
                }}
                placeholder="Ask anything about your dataset..."
                className="flex-1 bg-transparent border-none focus:outline-none resize-none max-h-32 min-h-[32px] py-1.5 font-body-main text-body-main text-on-surface placeholder:text-outline"
              />
              {busy ? (
                <button
                  type="button"
                  onClick={stop}
                  aria-label="Stop generating"
                  className="w-8 h-8 rounded-lg bg-surface-container-high text-on-surface flex items-center justify-center hover:bg-surface-variant transition-colors shrink-0"
                >
                  <Icon name="stop_circle" size={16} />
                </button>
              ) : (
                <button
                  type="submit"
                  disabled={!input.trim()}
                  aria-label="Send question"
                  className="w-8 h-8 rounded-lg bg-primary-container text-white flex items-center justify-center hover:bg-primary-hover transition-colors disabled:opacity-40 shrink-0"
                >
                  <Icon name="arrow_upward" size={16} />
                </button>
              )}
            </form>
          </div>
        </div>

        {/* Analysis panel */}
        <aside className="hidden xl:flex w-[440px] shrink-0 flex-col border-l border-outline-variant bg-surface-container-lowest">
          <div className="flex items-center justify-between gap-sm px-md py-sm border-b border-outline-variant">
            <h3 className="font-section-title text-section-title text-on-surface flex items-center gap-sm">
              <Icon name="insert_chart" size={20} className="text-primary" />
              Analysis Results
            </h3>
            <div className="flex items-center gap-sm">
              {latest?.spec?.chart && <StatusChip status={latest.spec.chart} />}
              <IconButton
                onClick={exportFigures}
                disabled={!latest}
                aria-label="Export these figures" icon="download" />
            </div>
          </div>

          <div className="flex-1 overflow-y-auto p-lg">
            <AnalysisPanel answer={latest} />
          </div>
        </aside>
      </div>

      <Modal
        open={saveName !== null}
        onClose={() => setSaveName(null)}
        title="Save this conversation"
        description="Name it so you can find it again in History."
        footer={
          <>
            <Button
              onClick={() => setSaveName(null)} variant="secondary">Cancel</Button>
            <Button
              onClick={saveSession}
              disabled={saving || !saveName?.trim()} variant="primary">
              {saving ? 'Saving…' : 'Save conversation'}
            </Button>
          </>
        }
      >
        <label className="sr-only" htmlFor="session-name">
          Conversation name
        </label>
        <input
          id="session-name"
          autoFocus
          value={saveName ?? ''}
          onChange={(e) => setSaveName(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && saveSession()}
          placeholder="Marks by subject, term 1"
          className="w-full h-[32px] rounded-lg border border-outline-variant bg-surface-container-lowest px-3 font-body-main text-body-main text-on-surface placeholder:text-outline focus:outline-none focus:border-primary-container transition-colors"
        />
      </Modal>

      <Modal
        open={confirmDelete}
        onClose={() => setConfirmDelete(false)}
        title="Delete this conversation?"
        description={`"${currentTitle}" and every turn in it will be removed, along with the specs and figures behind each answer. This cannot be undone.`}
        footer={
          <>
            <Button
              onClick={() => setConfirmDelete(false)} variant="secondary">Keep it</Button>
            <Button
              onClick={deleteSession} variant="danger">
              Delete conversation
            </Button>
          </>
        }
      />
    </FixedCanvas>
  )
}

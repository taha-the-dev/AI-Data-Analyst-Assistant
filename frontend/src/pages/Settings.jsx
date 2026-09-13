import { useCallback, useEffect, useState } from 'react'
import { PageCanvas, PageHeader } from '../components/AppShell'
import { Button, Panel, PanelHeader, PanelSkeleton, ErrorState, SegmentedControl } from '../components/ui'
import Icon from '../components/Icon'
import Modal from '../components/Modal'
import { useToast } from '../components/Toast'
import { api } from '../lib/api'
import { useResource } from '../hooks/useResource'
import { useAppearance } from '../context/AppContext'
import { useAuth } from '../context/AuthContext'

function Row({ label, description, children }) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-md py-md border-b border-outline-variant/40 last:border-0">
      <div className="min-w-0">
        <p className="font-label-bold text-label-bold text-on-surface">{label}</p>
        {description && <p className="font-body-sm text-body-sm text-on-surface-variant mt-xs max-w-md">{description}</p>}
      </div>
      <div className="shrink-0">{children}</div>
    </div>
  )
}

// Where each hosted provider's key comes from, and the variable the API reads
// it from. The panel used to give Gemini's instructions whichever provider was
// selected.
const KEY_HELP = {
  gemini: {
    url: 'https://aistudio.google.com/apikey',
    label: 'aistudio.google.com/apikey',
    variable: 'GEMINI_API_KEY',
  },
  openrouter: {
    url: 'https://openrouter.ai/keys',
    label: 'openrouter.ai/keys',
    variable: 'OPENROUTER_API_KEY',
  },
}

const selectCls =
  'h-9 bg-surface border border-outline-variant rounded-lg px-sm font-body-main text-body-main text-on-surface focus:outline-none focus:border-primary focus:ring-1 focus:ring-primary transition-all'

export default function Settings() {
  const toast = useToast()
  const settings = useResource(() => api.settings.get(), [])
  const providers = useResource(() => api.settings.providers(), [])
  const { appearance, theme, setAppearance } = useAppearance()
  const { account, signOut, deleteAccount } = useAuth()

  const [form, setForm] = useState(null)
  const [saving, setSaving] = useState(false)
  const [signingOut, setSigningOut] = useState(false)
  // null while the dialog is closed; otherwise what it is holding.
  const [deleting, setDeleting] = useState(null)

  // Stable, because the dialog moves focus back to its first button whenever
  // its close handler changes — which, inline, would be on every keystroke.
  const closeDelete = useCallback(() => setDeleting((current) => (current?.pending ? current : null)), [])

  useEffect(() => {
    if (settings.data) setForm(settings.data)
  }, [settings.data])

  const set = (patch) => setForm((f) => ({ ...f, ...patch }))

  const provider = providers.data?.find((p) => p.id === form?.providerId)

  const changeProvider = (id) => {
    const next = providers.data.find((p) => p.id === id)
    // Switching provider must also move to a model it actually serves, or the
    // API will reject the save.
    set({ providerId: id, modelName: next.models[0] })
  }

  const save = async () => {
    setSaving(true)
    try {
      const saved = await api.settings.save(form)
      setForm(saved)
      toast('Settings saved.')
    } catch (cause) {
      toast(cause.detail || cause.title, 'error')
    } finally {
      setSaving(false)
    }
  }

  const signOutNow = async () => {
    setSigningOut(true)
    try {
      await signOut()
    } catch (cause) {
      toast(cause?.detail || cause?.title || 'Could not sign out.', 'error')
      setSigningOut(false)
    }
  }

  const confirmDelete = async (event) => {
    event.preventDefault()
    const password = deleting.password
    setDeleting((current) => ({ ...current, pending: true, error: null }))
    try {
      await deleteAccount(password)
      // Signed out: the app hands over to the sign-in page.
    } catch (cause) {
      setDeleting((current) => current && { ...current, pending: false, password: '', error: cause })
    }
  }

  const header = (
    <PageHeader
      title="Settings"
      action={
        <Button variant="primary" icon="check" disabled={!form || saving} onClick={save}>
          {saving ? 'Saving…' : 'Save changes'}
        </Button>
      }
    />
  )

  if (settings.error || providers.error) {
    return (
      <PageCanvas>
        {header}
        <ErrorState
          error={settings.error ?? providers.error}
          onRetry={() => { settings.reload(); providers.reload() }}
        />
      </PageCanvas>
    )
  }

  if (!form || providers.loading) {
    return (
      <PageCanvas>
        {header}
        <PanelSkeleton className="h-[200px]" />
        <PanelSkeleton className="h-[240px]" />
      </PageCanvas>
    )
  }

  return (
    <PageCanvas>
      {header}

      <Panel className="p-md">
        <PanelHeader title="Account" />
        <div className="mt-md flex flex-col">
          <Row label="Signed in as" description={account?.email}>
            <Button icon="logout" onClick={signOutNow} disabled={signingOut}>
              {signingOut ? 'Signing out…' : 'Sign out'}
            </Button>
          </Row>
          <Row
            label="Delete account"
            description="Removes this account and every file, report and conversation in it."
          >
            <Button
              variant="quiet"
              icon="delete"
              onClick={() => setDeleting({ password: '', pending: false, error: null })}
            >
              Delete account
            </Button>
          </Row>
        </div>
      </Panel>

      <Panel className="p-md">
        <PanelHeader title="Appearance" />
        <div className="mt-md flex flex-col">
          {/* Appearance is stored on this device, not on the account: the same
              figures want a different answer on a laptop at night and a
              monitor at noon. It saves the moment it is picked, so it is the
              one row on this screen that does not wait for Save changes. */}
          <Row
            label="Theme"
            description={
              appearance === 'system'
                ? `Following this device, which is currently ${theme}.`
                : 'Applies on this device only, and takes effect straight away.'
            }
          >
            <SegmentedControl
              label="Theme"
              value={appearance}
              onChange={setAppearance}
              options={[
                { value: 'light', label: 'Light' },
                { value: 'dark', label: 'Dark' },
                { value: 'system', label: 'System' },
              ]}
            />
          </Row>
        </div>
      </Panel>

      <Panel className="p-md">
        <PanelHeader title="Model provider" />
        <div className="mt-md flex flex-col">
          <div className="grid grid-cols-1 md:grid-cols-2 gap-md pb-md">
            {providers.data.map((p) => {
              const active = p.id === form.providerId
              return (
                <button
                  key={p.id}
                  onClick={() => changeProvider(p.id)}
                  aria-pressed={active}
                  className={`text-left border rounded-xl p-md transition-colors ${
                    active
                      ? 'border-primary-container bg-primary-fixed/30'
                      : 'border-outline-variant bg-surface hover:bg-surface-container-low'
                  }`}
                >
                  <span className="flex items-center justify-between gap-sm">
                    <span className="font-label-bold text-label-bold text-on-surface">{p.name}</span>
                    <span
                      className={`inline-flex items-center px-sm py-[2px] rounded-full text-[12px] font-label-bold ${
                        p.status === 'Connected'
                          ? // The "fixed" Material pair is theme-invariant by
                            // definition, which put a bright ice-blue pill on a
                            // dark card. A connected provider is the same good
                            // news the rest of the app calls success, so it says
                            // it the same way.
                            'bg-success-container text-success'
                          : 'bg-warning-container/40 text-on-warning-container'
                      }`}
                    >
                      {p.status}
                    </span>
                  </span>
                  <span className="block font-body-sm text-body-sm text-on-surface-variant mt-xs">{p.detail}</span>
                </button>
              )
            })}
          </div>

          <Row label="Model" description="Used for planning queries and writing explanations.">
            <select
              value={form.modelName}
              onChange={(e) => set({ modelName: e.target.value })}
              aria-label="Model"
              className={`${selectCls} font-code text-code`}
            >
              {provider?.models.map((m) => <option key={m}>{m}</option>)}
            </select>
          </Row>

          {provider?.status === 'Needs key' && KEY_HELP[provider.id] && (
            <div className="flex items-start gap-sm bg-warning-container/20 border border-warning-container rounded-lg p-md mt-md">
              <Icon name="key" size={18} className="text-on-warning-container mt-0.5" />
              <div className="font-body-main text-body-main text-on-surface-variant space-y-sm">
                <p>
                  {provider.name} has no key yet, so questions are answered by the built-in planner
                  instead. Everything keeps working — the wording is just plainer.
                </p>
                <p>
                  Get a key at{' '}
                  <a
                    href={KEY_HELP[provider.id].url}
                    target="_blank"
                    rel="noreferrer"
                    className="text-primary underline hover:text-surface-tint"
                  >
                    {KEY_HELP[provider.id].label}
                  </a>
                  , then set it before starting the API:
                </p>
                <code className="block bg-surface-container-lowest border border-outline-variant rounded p-sm font-code text-code text-on-surface overflow-x-auto">
                  set {KEY_HELP[provider.id].variable}=your-key-here
                </code>
              </div>
            </div>
          )}
        </div>
      </Panel>

      <Modal
        open={deleting !== null}
        onClose={closeDelete}
        title="Delete this account?"
        description="Every file, report and conversation in it is deleted with it. This cannot be undone."
        footer={
          <>
            <Button onClick={closeDelete} disabled={deleting?.pending}>
              Keep account
            </Button>
            <Button
              variant="danger"
              icon="delete"
              type="submit"
              form="delete-account"
              disabled={!deleting?.password || deleting?.pending}
            >
              {deleting?.pending ? 'Deleting…' : 'Delete account'}
            </Button>
          </>
        }
      >
        <form id="delete-account" onSubmit={confirmDelete} className="flex flex-col gap-xs">
          <label htmlFor="delete-password" className="font-label-bold text-label-bold text-on-surface">
            Enter your password to confirm
          </label>
          <input
            id="delete-password"
            type="password"
            autoComplete="current-password"
            maxLength={256}
            value={deleting?.password ?? ''}
            onChange={(event) => {
              const password = event.target.value
              setDeleting((current) => current && { ...current, password })
            }}
            aria-invalid={deleting?.error ? true : undefined}
            aria-describedby={deleting?.error ? 'delete-password-error' : undefined}
            className={`${selectCls} w-full`}
          />
          {deleting?.error && (
            <p id="delete-password-error" role="alert" className="font-body-sm text-body-sm text-error">
              {deleting.error.title}
              {deleting.error.detail ? ` ${deleting.error.detail}` : ''}
            </p>
          )}
        </form>
      </Modal>
    </PageCanvas>
  )
}

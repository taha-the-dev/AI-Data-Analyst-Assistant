import { useState } from 'react'
import { PageCanvas, PageHeader } from '../components/AppShell'
import { Button, Panel, PanelHeader, SegmentedControl } from '../components/ui'
import Modal from '../components/Modal'
import { useToast } from '../components/Toast'
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

const inputCls =
  'h-9 bg-surface border border-outline-variant rounded-lg px-sm font-body-main text-body-main text-on-surface focus:outline-none focus:border-primary focus:ring-1 focus:ring-primary transition-all'

export default function Settings() {
  const toast = useToast()
  const { appearance, theme, setAppearance } = useAppearance()
  const { account, signOut, deleteAccount } = useAuth()

  const [signingOut, setSigningOut] = useState(false)
  // null while the dialog is closed; otherwise what it is holding.
  const [deleting, setDeleting] = useState(null)

  const closeDelete = () => setDeleting((current) => (current?.pending ? current : null))

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

  return (
    <PageCanvas>
      <PageHeader title="Settings" />

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
              monitor at noon. It saves the moment it is picked. */}
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
            className={`${inputCls} w-full`}
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

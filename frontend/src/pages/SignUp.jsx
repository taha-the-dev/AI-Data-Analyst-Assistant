import { useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import AuthLayout, { FormError, SubmitButton, TextField } from '../components/AuthLayout'
import { useAuth } from '../context/AuthContext'

/** Matches the API's rule; the API checks again, so this only saves a round trip. */
const MIN_PASSWORD_LENGTH = 12

const FIELD_IDS = { email: 'email', password: 'new-password', confirm: 'confirm-password' }

export default function SignUp() {
  const { signUp } = useAuth()
  const location = useLocation()

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [attempted, setAttempted] = useState(false)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState(null)

  const problems = {
    email: /^[^\s@]+@[^\s@]+$/.test(email.trim()) ? null : 'Enter an email address, such as name@example.com.',
    password:
      password.length >= MIN_PASSWORD_LENGTH ? null : `Use at least ${MIN_PASSWORD_LENGTH} characters.`,
    confirm: confirm === password ? null : 'The two passwords do not match.',
  }
  // Field messages wait for the first attempt, so nobody is told off mid-typing.
  const shown = attempted ? problems : {}

  const submit = async (event) => {
    event.preventDefault()
    setAttempted(true)
    setError(null)

    const firstInvalid = Object.keys(FIELD_IDS).find((field) => problems[field])
    if (firstInvalid) {
      document.getElementById(FIELD_IDS[firstInvalid])?.focus()
      return
    }

    setPending(true)
    try {
      await signUp(email.trim(), password)
      // Signed in: the route sends the new account on to the app.
    } catch (cause) {
      setError(cause)
      setPending(false)
    }
  }

  return (
    <AuthLayout
      title="Create your account"
      description="Your files, reports and conversations stay private to it."
      footer={
        <>
          Already have an account?{' '}
          <Link to="/login" state={location.state} className="font-label-bold text-primary hover:underline">
            Sign in
          </Link>
        </>
      }
    >
      <form onSubmit={submit} noValidate className="flex flex-col gap-md">
        <FormError error={error} />

        <TextField
          id={FIELD_IDS.email}
          label="Email"
          type="email"
          inputMode="email"
          autoComplete="email"
          autoFocus
          required
          maxLength={254}
          value={email}
          error={shown.email}
          onChange={(event) => setEmail(event.target.value)}
        />

        <TextField
          id={FIELD_IDS.password}
          label="Password"
          type="password"
          reveal
          autoComplete="new-password"
          required
          maxLength={256}
          value={password}
          hint={`At least ${MIN_PASSWORD_LENGTH} characters. A few unrelated words are long and easy to remember.`}
          error={shown.password}
          onChange={(event) => setPassword(event.target.value)}
        />

        <TextField
          id={FIELD_IDS.confirm}
          label="Confirm password"
          type="password"
          reveal
          autoComplete="new-password"
          required
          maxLength={256}
          value={confirm}
          error={shown.confirm}
          onChange={(event) => setConfirm(event.target.value)}
        />

        <p className="font-body-sm text-body-sm text-on-surface-variant">
          There is no password reset yet, so keep this password somewhere safe.
        </p>

        <SubmitButton pending={pending} disabled={pending}>
          {pending ? 'Creating account…' : 'Create account'}
        </SubmitButton>
      </form>
    </AuthLayout>
  )
}

import { useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import AuthLayout, { FormError, SubmitButton, TextField } from '../components/AuthLayout'
import { useAuth } from '../context/AuthContext'

export default function SignIn() {
  const { signIn } = useAuth()
  const location = useLocation()

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [pending, setPending] = useState(false)
  const [error, setError] = useState(null)

  const submit = async (event) => {
    event.preventDefault()
    setError(null)
    setPending(true)
    try {
      await signIn(email.trim(), password)
      // Signed in: the route sends the visitor on to wherever they were headed.
    } catch (cause) {
      setError(cause)
      setPassword('')
      setPending(false)
    }
  }

  return (
    <AuthLayout
      title="Sign in"
      description="Use the email and password for your DataMind account."
      footer={
        <>
          New to DataMind?{' '}
          <Link to="/signup" state={location.state} className="font-label-bold text-primary hover:underline">
            Create an account
          </Link>
        </>
      }
    >
      <form onSubmit={submit} noValidate className="flex flex-col gap-md">
        <FormError error={error} />

        <TextField
          id="email"
          label="Email"
          type="email"
          inputMode="email"
          autoComplete="email"
          autoFocus
          required
          maxLength={254}
          value={email}
          onChange={(event) => setEmail(event.target.value)}
        />

        <TextField
          id="password"
          label="Password"
          type="password"
          reveal
          autoComplete="current-password"
          required
          maxLength={256}
          value={password}
          onChange={(event) => setPassword(event.target.value)}
        />

        <SubmitButton pending={pending} disabled={pending || !email.trim() || !password}>
          {pending ? 'Signing in…' : 'Sign in'}
        </SubmitButton>
      </form>
    </AuthLayout>
  )
}

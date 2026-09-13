import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react'
import { api, SIGNED_OUT_EVENT } from '../lib/api'
import { forgetActiveDataset } from './AppContext'

/* --------------------------------------------------------------------------
   The signed-in account.

   The session is an HttpOnly cookie the page cannot read, so the only way to
   know whether one exists is to ask the API. Until it answers the app shows a
   loading state and nothing else: no screen renders a frame of data and then
   bounces to the sign-in page.
-------------------------------------------------------------------------- */

const AuthContext = createContext(null)

const SIGNED_OUT = { status: 'signed-out', account: null, error: null }

export function AuthProvider({ children }) {
  const [state, setState] = useState({ status: 'loading', account: null, error: null })

  const refresh = useCallback(async () => {
    setState({ status: 'loading', account: null, error: null })
    try {
      const account = await api.auth.me()
      setState({ status: 'signed-in', account, error: null })
    } catch (cause) {
      // 401 is the ordinary answer for a visitor with no session. Anything else
      // means the API could not say either way, which is a different state.
      setState(cause?.status === 401 ? SIGNED_OUT : { status: 'unavailable', account: null, error: cause })
    }
  }, [])

  useEffect(() => {
    refresh()
  }, [refresh])

  // Any 401 while signed in means the session is over: it expired, or it was
  // ended in another tab.
  useEffect(() => {
    const onSignedOut = () =>
      setState((current) => (current.status === 'signed-in' ? SIGNED_OUT : current))
    window.addEventListener(SIGNED_OUT_EVENT, onSignedOut)
    return () => window.removeEventListener(SIGNED_OUT_EVENT, onSignedOut)
  }, [])

  const signIn = useCallback(async (email, password) => {
    const account = await api.auth.signIn(email, password)
    setState({ status: 'signed-in', account, error: null })
  }, [])

  const signUp = useCallback(async (email, password) => {
    const account = await api.auth.signUp(email, password)
    setState({ status: 'signed-in', account, error: null })
  }, [])

  // Reported as signed out only once the API has ended the session: on a shared
  // machine, a sign-out that failed must not look like one that worked.
  const signOut = useCallback(async () => {
    await api.auth.signOut()
    forgetActiveDataset()
    setState(SIGNED_OUT)
  }, [])

  const deleteAccount = useCallback(async (password) => {
    await api.auth.deleteAccount(password)
    forgetActiveDataset()
    setState(SIGNED_OUT)
  }, [])

  const value = useMemo(
    () => ({ ...state, refresh, signIn, signUp, signOut, deleteAccount }),
    [state, refresh, signIn, signUp, signOut, deleteAccount]
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth() {
  const context = useContext(AuthContext)
  if (!context) throw new Error('useAuth must be used inside AuthProvider')
  return context
}

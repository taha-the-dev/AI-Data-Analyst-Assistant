import { useCallback, useEffect, useRef, useState } from 'react'

/**
 * Runs an async factory and tracks loading, error and data for it.
 *
 * Every screen needs the same three states, and the brief asks for all three to
 * be designed, so they are produced in one place rather than re-invented per
 * page. `reload` re-runs the factory — used by retry buttons and after a
 * mutation.
 */
export function useResource(factory, deps = []) {
  const [data, setData] = useState(null)
  const [error, setError] = useState(null)
  const [loading, setLoading] = useState(true)
  const [nonce, setNonce] = useState(0)

  // Held in a ref so an inline arrow factory does not retrigger the effect.
  const latest = useRef(factory)
  latest.current = factory

  // Resolved once the run a `reload` asked for has finished, so `await
  // reload()` means the data is here. Without it the call returned before the
  // fetch started and callers read the list they had just invalidated — which
  // is how deleting the last conversation re-selected the one it deleted.
  const settle = useRef(null)
  const finish = () => {
    settle.current?.()
    settle.current = null
  }

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    setError(null)

    latest
      .current()
      .then((result) => {
        if (!cancelled) setData(result)
      })
      .catch((cause) => {
        if (cancelled || cause?.name === 'AbortError') return
        setError(cause)
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
        finish()
      })

    return () => {
      cancelled = true
      // A superseded run still has to release whoever is waiting on it.
      finish()
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, nonce])

  const reload = useCallback(() => {
    finish()
    const done = new Promise((resolve) => {
      settle.current = resolve
    })
    setNonce((n) => n + 1)
    return done
  }, [])

  return { data, error, loading, reload }
}

import { createContext, useCallback, useContext, useMemo, useState } from 'react'
import Icon from './Icon'

const ToastContext = createContext(() => {})

export const useToast = () => useContext(ToastContext)

const tones = {
  success: { icon: 'check_circle', cls: 'text-success' },
  error: { icon: 'error', cls: 'text-error' },
  info: { icon: 'info', cls: 'text-primary' },
}

export function ToastProvider({ children }) {
  const [toasts, setToasts] = useState([])

  const push = useCallback((message, tone = 'success') => {
    const id = Date.now() + Math.random()
    setToasts((t) => [...t, { id, message, tone }])
    setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), 4000)
  }, [])

  const value = useMemo(() => push, [push])

  return (
    <ToastContext.Provider value={value}>
      {children}
      <div
        className="fixed bottom-md right-md left-md sm:left-auto z-[70] flex flex-col gap-sm sm:w-[360px]"
        role="status"
        aria-live="polite"
      >
        {toasts.map((t) => {
          const tone = tones[t.tone]
          return (
            <div
              key={t.id}
              className="flex items-start gap-sm bg-surface-container-lowest border border-outline-variant rounded-lg ambient-shadow px-md py-sm animate-slide-in"
            >
              <Icon name={tone.icon} size={18} className={`${tone.cls} mt-0.5`} />
              <span className="font-body-main text-body-main text-on-surface">{t.message}</span>
            </div>
          )
        })}
      </div>
    </ToastContext.Provider>
  )
}

import { useEffect, useState } from 'react'
import Icon from './Icon'

/**
 * The frame the sign-in and sign-up pages share.
 *
 * On a wide screen the left half is the rail's own charcoal, so signing in
 * reads as stepping into the app rather than past a separate gate. What it says
 * is how the product actually works — the three stages every answer goes
 * through — which is a real sequence, so it is numbered.
 */

const STAGES = [
  ['Plan', 'Your question becomes a query: which column, which measure, which filter.'],
  ['Compute', 'The engine runs that query against your own stored rows. No figure is guessed.'],
  ['Explain', 'The answer is written from the computed figures, with the query beside it.'],
]

function Brand({ onDark = false }) {
  return (
    <div className="flex items-center gap-sm">
      <span
        aria-hidden="true"
        className="w-8 h-8 rounded-lg bg-primary-container text-white flex items-center justify-center font-label-bold text-[13px] shrink-0"
      >
        DM
      </span>
      <span
        className={`font-page-title text-[15px] leading-[20px] font-semibold ${onDark ? 'text-white' : 'text-on-surface'}`}
      >
        DataMind AI
      </span>
    </div>
  )
}

export default function AuthLayout({ title, description, children, footer }) {
  useEffect(() => {
    document.title = `${title} — DataMind AI`
  }, [title])

  return (
    <div className="min-h-screen bg-surface lg:grid lg:grid-cols-[minmax(0,5fr)_minmax(0,6fr)]">
      <aside className="on-dark nav-surface hidden lg:flex flex-col justify-between gap-xl p-xl">
        <Brand onDark />

        <div className="max-w-md">
          <p className="font-page-title text-[30px] leading-[36px] font-semibold text-white">
            Upload a file.
            <br />
            Ask it anything.
          </p>
          <p className="mt-md font-body-main text-body-main text-secondary-fixed-dim">
            Every figure is computed from your own rows, and every answer carries the query that produced it.
          </p>

          <ol className="mt-xl flex flex-col gap-md">
            {STAGES.map(([name, text], index) => (
              <li key={name} className="flex gap-md">
                <span className="w-5 shrink-0 pt-[1px] font-code text-code text-secondary-fixed-dim tabular-nums">
                  {index + 1}
                </span>
                <span>
                  <span className="block font-label-bold text-label-bold text-white">{name}</span>
                  <span className="mt-[2px] block font-body-sm text-body-sm text-secondary-fixed-dim">{text}</span>
                </span>
              </li>
            ))}
          </ol>
        </div>

        <p className="font-body-sm text-body-sm text-secondary-fixed-dim">
          Files, reports and conversations are visible only to the account that created them.
        </p>
      </aside>

      <main className="flex min-h-screen items-center justify-center px-md py-xl">
        <div className="w-full max-w-sm animate-fade-up">
          <div className="mb-lg lg:hidden">
            <Brand />
          </div>

          <h1 className="font-page-title text-page-title text-on-surface">{title}</h1>
          {description && (
            <p className="mt-xs font-body-main text-body-main text-on-surface-variant">{description}</p>
          )}

          <div className="mt-lg">{children}</div>

          {footer && <p className="mt-lg font-body-main text-body-main text-on-surface-variant">{footer}</p>}
        </div>
      </main>
    </div>
  )
}

/** A labelled input. `reveal` adds a show/hide control for passwords. */
export function TextField({ id, label, type = 'text', hint, error, reveal = false, ...input }) {
  const [shown, setShown] = useState(false)
  const describedBy = [hint && !error && `${id}-hint`, error && `${id}-error`].filter(Boolean).join(' ') || undefined

  return (
    <div className="flex flex-col gap-xs">
      <label htmlFor={id} className="font-label-bold text-label-bold text-on-surface">
        {label}
      </label>
      <div className="relative">
        <input
          id={id}
          type={reveal && shown ? 'text' : type}
          aria-invalid={error ? true : undefined}
          aria-describedby={describedBy}
          className={`h-10 w-full rounded-lg border bg-surface-container-lowest px-3 ${reveal ? 'pr-11' : ''} font-body-main text-body-main text-on-surface placeholder:text-outline transition-colors focus:outline-none focus:ring-2 focus:ring-primary-container/40 ${
            error ? 'border-error' : 'border-outline-variant focus:border-primary-container'
          }`}
          {...input}
        />
        {reveal && (
          <button
            type="button"
            onClick={() => setShown((value) => !value)}
            aria-label={shown ? 'Hide password' : 'Show password'}
            aria-pressed={shown}
            aria-controls={id}
            className="absolute inset-y-0 right-0 w-10 flex items-center justify-center rounded-r-lg text-on-surface-variant hover:text-on-surface transition-colors"
          >
            <Icon name={shown ? 'visibility_off' : 'visibility'} size={18} />
          </button>
        )}
      </div>
      {hint && !error && (
        <p id={`${id}-hint`} className="font-body-sm text-body-sm text-on-surface-variant">
          {hint}
        </p>
      )}
      {error && (
        <p id={`${id}-error`} className="font-body-sm text-body-sm text-error">
          {error}
        </p>
      )}
    </div>
  )
}

/** The API's own title and detail, which say what went wrong and what to do. */
export function FormError({ error }) {
  if (!error) return null
  return (
    <div role="alert" className="flex items-start gap-sm rounded-lg border border-error-container bg-error-container/20 p-md">
      <Icon name="error" size={18} className="mt-[1px] shrink-0 text-error" />
      <div className="min-w-0">
        <p className="font-label-bold text-label-bold text-on-surface">{error.title ?? 'Something went wrong'}</p>
        {error.detail && <p className="mt-xs font-body-sm text-body-sm text-on-surface-variant">{error.detail}</p>}
      </div>
    </div>
  )
}

export function SubmitButton({ pending = false, children, ...rest }) {
  return (
    <button
      type="submit"
      aria-busy={pending || undefined}
      className="mt-xs inline-flex h-10 w-full items-center justify-center gap-sm rounded-lg bg-primary-container px-md font-label-bold text-label-bold text-white transition-colors hover:bg-primary-hover disabled:cursor-not-allowed disabled:opacity-40"
      {...rest}
    >
      {children}
    </button>
  )
}

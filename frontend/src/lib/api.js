/**
 * The one place the frontend talks to the API.
 *
 * In dev the base is a relative /api, proxied by Vite to the .NET service, so
 * the browser sees a single origin. That is required, not just convenient: the
 * session is a SameSite=Strict cookie, so the app and the API must share an
 * origin. VITE_API_BASE only changes the path the API is mounted at.
 */
const BASE = import.meta.env.VITE_API_BASE ?? '/api'

/**
 * Sent on every request that changes data, and required by the API for them. A
 * page on another site can make a browser post a form with the visitor's
 * cookie, but it cannot add a header.
 */
const CSRF_HEADER = 'X-DataMind-Csrf'

/** Fired whenever the API answers 401: the session expired or ended elsewhere. */
export const SIGNED_OUT_EVENT = 'datamind:signed-out'

/** Carries the server's ProblemDetails so a screen can say what to do next. */
export class ApiError extends Error {
  constructor({ title, detail, status }) {
    super(title)
    this.name = 'ApiError'
    this.title = title
    this.detail = detail ?? ''
    this.status = status ?? 0
  }
}

/** The request never reached the service, whatever stopped it. */
function unreachableError() {
  return new ApiError({
    title: 'Cannot reach the API',
    detail: import.meta.env.DEV
      ? 'The service is not responding. Start it with "dotnet run --project backend/AnalystAI.Api", then retry.'
      : 'The service is not available right now. Try again in a moment.',
    status: 0,
  })
}

async function request(path, { method = 'GET', body, signal } = {}) {
  const isForm = body instanceof FormData
  let response

  try {
    const headers = {}
    if (!isForm && body !== undefined) headers['Content-Type'] = 'application/json'
    if (method !== 'GET') headers[CSRF_HEADER] = '1'

    response = await fetch(`${BASE}${path}`, {
      method,
      signal,
      // The session is an HttpOnly cookie on this origin; nothing here reads it.
      credentials: 'same-origin',
      headers,
      body: isForm ? body : body === undefined ? undefined : JSON.stringify(body),
    })
  } catch (cause) {
    if (cause?.name === 'AbortError') throw cause
    throw unreachableError()
  }

  if (response.status === 401) window.dispatchEvent(new Event(SIGNED_OUT_EVENT))

  if (response.status === 204) return null

  const text = await response.text()
  let payload = null
  try {
    payload = text ? JSON.parse(text) : null
  } catch {
    payload = null
  }

  // The API answers everything in JSON, errors included. A body that is not —
  // an HTML page, a static host's own 404 — means the request never reached
  // the service: the dev proxy could not connect, or a deployed frontend has no
  // route to it yet. Taking that page as an answer would read as signed in.
  if (text !== '' && payload === null) throw unreachableError()

  if (!response.ok) {
    // A ProblemDetails body means the API answered and explained itself.
    // Anything else at a gateway status means it was never reached.
    const answered = Boolean(payload?.title)
    if (!answered && (response.status >= 500 || response.status === 404)) throw unreachableError()

    throw new ApiError({
      title: payload?.title ?? `Request failed (${response.status})`,
      detail: payload?.detail ?? '',
      status: response.status,
    })
  }

  return payload
}

const qs = (params) => {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null || value === '') continue
    if (Array.isArray(value)) value.forEach((v) => search.append(key, v))
    else search.append(key, value)
  }
  const s = search.toString()
  return s ? `?${s}` : ''
}

export const api = {
  health: () => request('/health'),
  /** What the signed-in account has loaded, and which planner will answer. */
  status: () => request('/status'),

  auth: {
    me: () => request('/auth/me'),
    signUp: (email, password) =>
      request('/auth/signup', { method: 'POST', body: { email, password } }),
    signIn: (email, password) =>
      request('/auth/login', { method: 'POST', body: { email, password } }),
    signOut: () => request('/auth/logout', { method: 'POST' }),
    deleteAccount: (password) => request('/auth/account', { method: 'DELETE', body: { password } }),
  },

  datasets: {
    list: (params = {}) => request(`/datasets${qs(params)}`),
    get: (id) => request(`/datasets/${id}`),
    remove: (id) => request(`/datasets/${id}`, { method: 'DELETE' }),
    upload: (file) => {
      const form = new FormData()
      form.append('file', file)
      return request('/datasets/upload', { method: 'POST', body: form })
    },
  },

  explorer: {
    columns: () => request('/explorer/columns'),
    rows: (params = {}) => request(`/explorer/rows${qs(params)}`),
  },

  dashboard: (datasetId) => request(`/dashboard${qs({ datasetId })}`),
  analytics: (datasetId) => request(`/analytics${qs({ datasetId })}`),

  chat: {
    sessions: () => request('/chat/sessions'),
    create: (title) => request('/chat/sessions', { method: 'POST', body: { title } }),
    messages: (id) => request(`/chat/sessions/${id}`),
    /** Saving a conversation is naming it — every turn is stored as it happens. */
    save: (id, title, subtitle) =>
      request(`/chat/sessions/${id}`, { method: 'PUT', body: { title, subtitle } }),
    remove: (id) => request(`/chat/sessions/${id}`, { method: 'DELETE' }),
    ask: (id, question, datasetId) =>
      request(`/chat/sessions/${id}/ask${qs({ datasetId })}`, { method: 'POST', body: { question } }),
    /** URL for the EventSource stream — plan, figures, tokens, done. */
    streamUrl: (id, question, datasetId) =>
      `${BASE}/chat/sessions/${id}/stream${qs({ question, datasetId })}`,
  },

  runSpec: (spec, datasetId) => request('/query/run', { method: 'POST', body: { spec, datasetId } }),

  reports: {
    list: () => request('/reports'),
    get: (id) => request(`/reports/${id}`),
    /** Writes a report for a dataset; the body is composed from live figures on read. */
    create: (title, datasetId) => request('/reports', { method: 'POST', body: { title, datasetId } }),
    remove: (id) => request(`/reports/${id}`, { method: 'DELETE' }),
  },

}

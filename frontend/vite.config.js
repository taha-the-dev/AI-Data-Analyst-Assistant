import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

const API_TARGET = process.env.VITE_API_TARGET ?? 'http://localhost:5180'

/**
 * What the proxy does when the API is not running.
 *
 * Vite's default is to log an `AggregateError [ECONNREFUSED]` stack per failed
 * request — a dozen of them on first paint, none of which says what to start.
 * This logs the cause once and answers the request with the same ProblemDetails
 * shape the API itself uses, so the screen shows "Cannot reach the API" with the
 * command to fix it instead of a proxy error page.
 */
const explainOutage = (proxy) => {
  let warned = false

  proxy.on('error', (error, _request, response) => {
    if (!warned) {
      warned = true
      console.warn(
        `\n  The API at ${API_TARGET} is not answering (${error.code ?? error.message}).` +
          '\n  Start it in another terminal:' +
          '\n    dotnet run --project backend/AnalystAI.Api --urls http://localhost:5180' +
          '\n  or run start.cmd from the project root to launch both.\n'
      )
    }

    if (response.writableEnded || response.headersSent) return

    response.writeHead(503, { 'Content-Type': 'application/problem+json' })
    response.end(
      JSON.stringify({
        title: 'Cannot reach the API',
        detail:
          `Nothing is listening on ${API_TARGET}. Start it with "dotnet run --project ` +
          'backend/AnalystAI.Api --urls http://localhost:5180", then retry.',
        status: 503,
      })
    )
  })

  // One line when it comes back, so a recovered API is visible in the terminal.
  proxy.on('proxyRes', () => {
    if (!warned) return
    warned = false
    console.log(`\n  The API at ${API_TARGET} is answering again.\n`)
  })
}

// The API is proxied so the browser only ever talks to one origin. No CORS
// preflight, no base URL to configure, and the API reference stays reachable at
// /swagger wherever the API happens to be running. The same table serves the dev
// server and the preview server, so a build behaves like the thing that was
// developed.
const proxy = {
  '/api': { target: API_TARGET, changeOrigin: true, configure: explainOutage },
  '/swagger': { target: API_TARGET, changeOrigin: true, configure: explainOutage },
  '/openapi': { target: API_TARGET, changeOrigin: true, configure: explainOutage },
}

// A tunnel gives the site a hostname Vite has never heard of, and Vite refuses
// requests for unknown hosts. These are the domains ngrok hands out.
const tunnelHosts = ['.ngrok-free.app', '.ngrok.app', '.ngrok.io', '.ngrok-free.dev']

// Shipped as one cached chunk: these change when React does, not when the app
// does.
const reactChunk = ['react', 'react-dom', 'react-router', 'react-router-dom', 'scheduler']

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    allowedHosts: tunnelHosts,
    proxy,
  },
  // `npm run preview` serves the built site: what gets shared over a tunnel is
  // the production bundle, not a dev server with a hot-reload socket trying to
  // reach a machine nobody else can see.
  preview: {
    port: 4173,
    allowedHosts: tunnelHosts,
    proxy,
  },
  build: {
    rollupOptions: {
      output: {
        // React and the router move on their own release cycle, so they are
        // split into a chunk that stays cached across app deploys. Written as
        // a function because the bundler behind Vite 8 no longer accepts the
        // object form.
        manualChunks: (id) => {
          // Module ids arrive posix-separated whatever the host OS is.
          const parts = id.split('/node_modules/')
          if (parts.length < 2) return undefined
          return reactChunk.includes(parts.at(-1).split('/')[0]) ? 'react' : undefined
        },
      },
    },
  },
})

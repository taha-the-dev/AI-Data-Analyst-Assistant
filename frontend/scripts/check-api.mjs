/**
 * Preflight for `npm run dev`.
 *
 * The frontend runs perfectly well with no API — it just shows "Cannot reach the
 * API" on every screen, which is easy to mistake for a broken build. This says
 * so in the terminal before the browser opens, and never fails the run: starting
 * the frontend alone is a legitimate thing to do.
 */
const target = process.env.VITE_API_TARGET ?? 'http://localhost:5180'

const reach = async () => {
  const controller = new AbortController()
  const timer = setTimeout(() => controller.abort(), 1500)

  try {
    const response = await fetch(`${target}/api/health`, { signal: controller.signal })
    return response.ok ? await response.json() : null
  } catch {
    return null
  } finally {
    clearTimeout(timer)
  }
}

const health = await reach()

if (health) {
  // Health is public and deliberately says nothing about what is stored; what an
  // account holds is only visible once it has signed in.
  console.log(`  API ${target} — ok`)
} else {
  console.warn(
    `\n  ⚠  The API at ${target} is not running.` +
      '\n     Every screen will show "Cannot reach the API" until it is.' +
      '\n' +
      '\n     Start it in another terminal:' +
      '\n       dotnet run --project backend/AnalystAI.Api --urls http://localhost:5180' +
      '\n     or run start.cmd from the project root to launch both.\n'
  )
}

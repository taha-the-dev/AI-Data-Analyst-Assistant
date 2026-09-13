// Mirrors src/lib/theme.js. Loaded as a blocking script in <head> so <html>
// carries the right class before the first paint — a deferred toggle shows a
// white flash on the way into a dark session. It lives in a file rather than
// inline so the Content-Security-Policy can refuse every inline script.
;(function () {
  try {
    var stored = localStorage.getItem('datamind.appearance')
    var dark =
      stored === 'dark' ||
      (stored !== 'light' && window.matchMedia('(prefers-color-scheme: dark)').matches)
    document.documentElement.classList.toggle('dark', dark)
    document.documentElement.classList.toggle('light', !dark)
  } catch (e) {
    // No storage, no matchMedia: light is the default and already set.
  }
})()

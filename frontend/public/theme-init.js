// Applies the last light/dark and motion choice before the app loads, so a dark-mode user never sees a white
// flash. A separate file because the content security policy allows no inline scripts. Mirrors src/lib/preferences.ts.
;(function () {
  var prefs = {}
  try {
    prefs = JSON.parse(localStorage.getItem('salesapp.prefs.device') || '{}') || {}
  } catch {
    prefs = {}
  }
  var media = function (q) {
    return window.matchMedia && window.matchMedia(q).matches
  }
  var dark = prefs.theme === 'dark' || (prefs.theme !== 'light' && media('(prefers-color-scheme: dark)'))
  var motion = prefs.motion === 'full' || prefs.motion === 'subtle' || prefs.motion === 'off' ? prefs.motion : media('(prefers-reduced-motion: reduce)') ? 'off' : 'full'
  var root = document.documentElement
  if (dark) root.classList.add('dark')
  root.style.colorScheme = dark ? 'dark' : 'light'
  root.setAttribute('data-motion', motion)
})()

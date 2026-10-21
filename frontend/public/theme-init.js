// Applies the last light/dark, motion and language choice before the app loads, so a dark-mode user never sees a white
// flash, and the company's colours, logo and name from last time. A separate file because the content security
// policy allows no inline scripts. Mirrors src/lib/preferences.ts and src/features/branding/apply.ts.
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
  root.lang = prefs.language === 'ta' ? 'ta' : 'en'

  var brand = null
  try {
    brand = JSON.parse(localStorage.getItem('salesapp.brand') || 'null')
  } catch {
    brand = null
  }
  if (!brand) return
  // Only the style sheet the app itself wrote: colours, nothing that could load or run anything.
  if (typeof brand.css === 'string' && brand.css && !/[<>]|url\(|@import|expression/i.test(brand.css)) {
    var style = document.createElement('style')
    style.id = 'brand-theme'
    style.textContent = brand.css
    document.head.appendChild(style)
  }
  if (typeof brand.title === 'string' && brand.title) document.title = brand.title
  if (typeof brand.logoUrl === 'string' && /^\/api\/branding\/logo\?v=\d+$/.test(brand.logoUrl)) {
    var icon = document.querySelector('link[rel="icon"]')
    if (icon) {
      icon.removeAttribute('type')
      icon.href = brand.logoUrl
    }
  }
})()

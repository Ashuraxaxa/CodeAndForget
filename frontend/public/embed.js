/*
 * Celdo · embed.js (P-138, D-113). Embebe dashboards y mantenedores de Celdo en otra página, sin dependencias.
 *
 *   <div id="ventas"></div>
 *   <script src="https://celdo.miempresa.com/embed.js"></script>
 *   <script>
 *     Celdo.embed('#ventas', {
 *       url: '/embed/dashboards/ID',            // o /embed/c/{conexión}/t/{esquema}/{tabla}, o /embed/p/{enlace}
 *       getToken: () => fetch('/celdo-token').then(r => r.text()),  // opcional: el servidor firma el token
 *       tema: 'sistema', idioma: 'es',
 *       onEvent: e => console.log(e.type, e)   // celdo:ready, celdo:saved, celdo:deleted, celdo:click, ...
 *     })
 *   </script>
 *
 * Con getToken, el token se pide al abrir y otra vez antes de que venza (celdo:token-expiring), sin recargar.
 * El iframe toma el alto del contenido (celdo:resize). Los mensajes solo se aceptan del iframe y de su origen.
 */
(function (window, document) {
  'use strict'
  var script = document.currentScript
  var base = script && script.src ? new URL('.', script.src).href.replace(/\/$/, '') : ''

  function embed(target, options) {
    options = options || {}
    var host = typeof target === 'string' ? document.querySelector(target) : target
    if (!host) throw new Error('Celdo.embed: no se encontró ' + target)
    if (!options.url) throw new Error('Celdo.embed: falta url, p. ej. /embed/dashboards/ID')
    var root = (options.base || base).replace(/\/$/, '')
    var origin = new URL(root || window.location.href).origin
    var iframe = document.createElement('iframe')
    iframe.title = options.title || 'Celdo'
    iframe.loading = options.loading || 'lazy'
    iframe.style.width = '100%'
    iframe.style.border = '0'
    iframe.style.display = 'block'
    iframe.style.height = (options.height || 400) + 'px'
    var autoHeight = options.autoHeight !== false

    function src(token) {
      var url = new URL(String(options.url).replace(/^\//, ''), root + '/') // relativa a donde está publicado Celdo
      if (options.tema) url.searchParams.set('tema', options.tema)
      if (options.idioma) url.searchParams.set('idioma', options.idioma)
      if (token) url.hash = 'token=' + encodeURIComponent(token)
      return url.href
    }

    function emit(e) {
      if (typeof options.onEvent === 'function') {
        try { options.onEvent(e) } catch (err) { console.error(err) }
      }
    }

    function send(message) {
      if (iframe.contentWindow) iframe.contentWindow.postMessage(message, origin)
    }

    function renew() {
      if (typeof options.getToken !== 'function') return
      Promise.resolve(options.getToken()).then(function (token) {
        if (token) send({ type: 'celdo:token', token: String(token).trim() })
      }, function (err) { emit({ type: 'celdo:error', error: String(err && err.message || err) }) })
    }

    function onMessage(e) {
      if (e.source !== iframe.contentWindow || e.origin !== origin || !e.data || typeof e.data.type !== 'string') return
      var data = e.data
      if (data.type === 'celdo:resize' && autoHeight && typeof data.height === 'number') iframe.style.height = Math.max(50, data.height) + 'px'
      if (data.type === 'celdo:token-expiring' || data.type === 'celdo:session-expired') renew()
      emit(data)
    }
    window.addEventListener('message', onMessage)

    if (options.token) iframe.src = src(options.token)
    else if (typeof options.getToken === 'function') {
      Promise.resolve(options.getToken()).then(function (token) { iframe.src = src(token) },
        function (err) { emit({ type: 'celdo:error', error: String(err && err.message || err) }) })
    } else iframe.src = src(null)
    host.appendChild(iframe)

    return {
      iframe: iframe,
      /** Envía un token nuevo sin recargar la página. */
      setToken: function (token) { send({ type: 'celdo:token', token: String(token) }) },
      /** Quita el iframe y deja de escuchar. */
      destroy: function () { window.removeEventListener('message', onMessage); iframe.remove() }
    }
  }

  window.Celdo = window.Celdo || {}
  window.Celdo.embed = embed
})(window, document)

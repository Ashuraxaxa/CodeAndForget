import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { UserManager, type User } from 'oidc-client-ts'
import { t } from '../i18n'
import { inFrame, notifyHost, signedToken } from '../lib/embed'
import type { EmbedSession } from '../api/types'
import { api, setTokenProvider, setUnauthorizedHandler, tokenStore, type AuthConfig } from '../api/client'

/**
 * Tres formas de sesión, según lo que configure la API (D-024):
 * - Local: usuario y contraseña contra la propia API.
 * - Jwt + ClientId: login en el proveedor OIDC (Duende, Keycloak, Entra ID…) con Authorization Code + PKCE.
 * - Jwt sin ClientId: un gateway/proxy autentica y agrega el token; la UI no hace login.
 */
interface AuthState {
  config: AuthConfig | null
  configError: string | null
  isAuthenticated: boolean
  /** Modo local. */
  login: (username: string, password: string) => Promise<void>
  /** Modo OIDC: redirige al proveedor (en un iframe, abre una ventana emergente: el proveedor no se deja embeber). */
  loginExternal: () => Promise<void>
  /** Modo OIDC: completa el login al volver del proveedor. */
  completeExternalLogin: () => Promise<void>
  logout: () => void
  /** Página embebida con el token de una aplicación (P-135): se está canjeando. */
  embedPending: boolean
  /** No se pudo canjear el token o la sesión embebida venció. */
  embedError: string | null
}

const EMBED_KEY = 'celdo.embed.session'

/** Sesión embebida guardada en esta pestaña (sobrevive a recargar el iframe, no a cerrarlo). */
function storedEmbed(): EmbedSession | null {
  try {
    const s = JSON.parse(sessionStorage.getItem(EMBED_KEY) ?? 'null') as EmbedSession | null
    return s && Date.parse(s.expiresAt) > Date.now() ? s : null
  } catch { return null }
}

function storeEmbed(s: EmbedSession | null) {
  try {
    if (s) sessionStorage.setItem(EMBED_KEY, JSON.stringify(s))
    else sessionStorage.removeItem(EMBED_KEY)
  } catch { /* sin almacenamiento: vale mientras la página esté abierta */ }
}

const AuthContext = createContext<AuthState | null>(null)

/** URL absoluta dentro de la app, respetando la subruta de publicación (<base href>). */
const appUrl = (path: string) => new URL(path, document.baseURI).href

export function AuthProvider({ children }: { children: ReactNode }) {
  const [config, setConfig] = useState<AuthConfig | null>(null)
  const [configError, setConfigError] = useState<string | null>(null)
  const [localToken, setLocalToken] = useState(() => tokenStore.get())
  const [oidcUser, setOidcUser] = useState<User | null>(null)
  const oidcUserRef = useRef<User | null>(null)
  // Con un token nuevo en la URL, manda ese; si no, la sesión embebida de esta pestaña, si sigue vigente.
  const [embed, setEmbed] = useState<EmbedSession | null>(() => (signedToken ? null : storedEmbed()))
  const embedRef = useRef(embed)
  const [embedPending, setEmbedPending] = useState(!!signedToken)
  const [embedError, setEmbedError] = useState<string | null>(null)

  useEffect(() => {
    if (!signedToken) return
    api.embedSession(signedToken)
      .then(s => { embedRef.current = s; storeEmbed(s); setEmbed(s) })
      .catch(e => setEmbedError((e as Error).message))
      .finally(() => setEmbedPending(false))
  }, [])

  useEffect(() => {
    api.authConfig().then(setConfig).catch(e => setConfigError((e as Error).message))
  }, [])

  const manager = useMemo(() => {
    if (config?.mode !== 'Jwt' || !config.clientId || !config.authority) return null
    return new UserManager({
      authority: config.authority,
      client_id: config.clientId,
      redirect_uri: appUrl('auth/callback'),
      post_logout_redirect_uri: appUrl(''),
      response_type: 'code',
      scope: config.scopes ?? 'openid profile',
      // Renueva antes de vencer: con refresh token si el proveedor lo entrega; si no, en un iframe oculto que vuelve a
      // silent-renew.html con la sesión del proveedor (P-079). Si el proveedor ya no tiene sesión, se vuelve a ingresar.
      automaticSilentRenew: true,
      silent_redirect_uri: appUrl('silent-renew.html'),
    })
  }, [config])

  const setUser = useCallback((u: User | null) => {
    oidcUserRef.current = u
    setOidcUser(u)
  }, [])

  // El cliente HTTP pide el token aquí: en OIDC vive en memoria (y en sessionStorage, manejado por oidc-client-ts).
  useEffect(() => {
    setTokenProvider(() => embedRef.current?.token ?? (manager ? oidcUserRef.current?.access_token ?? null : tokenStore.get()))
  }, [manager])

  useEffect(() => {
    if (!manager) return
    manager.getUser().then(u => setUser(u && !u.expired ? u : null))
    const loaded = (u: User) => setUser(u)
    const expired = () => setUser(null)
    manager.events.addUserLoaded(loaded)
    manager.events.addAccessTokenExpired(expired)
    return () => {
      manager.events.removeUserLoaded(loaded)
      manager.events.removeAccessTokenExpired(expired)
    }
  }, [manager, setUser])

  // Renovación (P-138): 2 minutos antes de vencer, se pide a la app anfitriona un token nuevo (celdo:token-expiring).
  // embed.js lo firma con getToken y lo envía como celdo:token; se canjea sin recargar la página.
  useEffect(() => {
    if (!embed) return
    const wait = Math.max(5_000, Date.parse(embed.expiresAt) - Date.now() - 120_000)
    const timer = setTimeout(() => notifyHost('celdo:token-expiring', { expiresAt: embed.expiresAt }), wait)
    return () => clearTimeout(timer)
  }, [embed])
  useEffect(() => {
    if (!embedRef.current && !signedToken) return
    const onMessage = (e: MessageEvent) => {
      if (e.source !== window.parent || e.data?.type !== 'celdo:token' || typeof e.data.token !== 'string') return
      api.embedSession(e.data.token)
        .then(s => { embedRef.current = s; storeEmbed(s); setEmbed(s); setEmbedError(null) })
        .catch(err => setEmbedError((err as Error).message))
    }
    window.addEventListener('message', onMessage)
    return () => window.removeEventListener('message', onMessage)
  }, [])

  const logout = useCallback(() => {
    // Sesión embebida: la otra app tiene que volver a firmar un token; no hay login propio que mostrar.
    if (embedRef.current) {
      embedRef.current = null
      storeEmbed(null)
      setEmbed(null)
      setEmbedError(t('La sesión de esta página venció. Vuelve a cargar la página.'))
      notifyHost('celdo:session-expired')
      return
    }
    tokenStore.set(null)
    setLocalToken(null)
    if (manager) {
      // signoutRedirect envía el id_token_hint (el proveedor cierra sin pedir confirmación) y borra el usuario local.
      const hadUser = !!oidcUserRef.current
      setUser(null)
      // En un iframe no se va al proveedor (no se deja embeber): se borra la sesión local y se vuelve a pedir el login.
      if (hadUser && !inFrame) {
        manager.signoutRedirect().catch(() => manager.removeUser())
      } else {
        manager.removeUser()
      }
    }
  }, [manager, setUser])

  useEffect(() => setUnauthorizedHandler(logout), [logout])

  const login = useCallback(async (username: string, password: string) => {
    const res = await api.login(username, password)
    tokenStore.set(res.token)
    setLocalToken(res.token)
  }, [])

  const loginExternal = useCallback(async () => {
    if (!manager) throw new Error(t('El proveedor de identidad no está configurado.'))
    if (inFrame) setUser(await manager.signinPopup({ popupWindowTarget: 'celdo-login' }))
    else await manager.signinRedirect()
  }, [manager, setUser])

  const completeExternalLogin = useCallback(async () => {
    if (!manager) throw new Error(t('El proveedor de identidad no está configurado.'))
    // signinCallback sirve para las dos vueltas: la de redirección y la de la ventana emergente (que se cierra sola).
    const user = await manager.signinCallback()
    if (user) setUser(user)
  }, [manager, setUser])

  const isAuthenticated = !!embed || (
    config?.mode === 'Local' ? !!localToken
      : config?.mode === 'Jwt' ? (manager ? !!oidcUser : true)
        : false)

  return (
    <AuthContext.Provider value={{ config, configError, isAuthenticated, login, loginExternal, completeExternalLogin, logout, embedPending, embedError }}>
      {children}
    </AuthContext.Provider>
  )
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth fuera de AuthProvider')
  return ctx
}

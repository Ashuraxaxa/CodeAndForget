import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { UserManager, type User } from 'oidc-client-ts'
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
  /** Modo OIDC: redirige al proveedor. */
  loginExternal: () => Promise<void>
  /** Modo OIDC: completa el login al volver del proveedor. */
  completeExternalLogin: () => Promise<void>
  logout: () => void
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
      // Renueva con refresh token si el proveedor lo entrega; si no, al vencer se vuelve a iniciar sesión.
      automaticSilentRenew: true,
    })
  }, [config])

  const setUser = useCallback((u: User | null) => {
    oidcUserRef.current = u
    setOidcUser(u)
  }, [])

  // El cliente HTTP pide el token aquí: en OIDC vive en memoria (y en sessionStorage, manejado por oidc-client-ts).
  useEffect(() => {
    setTokenProvider(() => (manager ? oidcUserRef.current?.access_token ?? null : tokenStore.get()))
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

  const logout = useCallback(() => {
    tokenStore.set(null)
    setLocalToken(null)
    if (manager) {
      // signoutRedirect envía el id_token_hint (el proveedor cierra sin pedir confirmación) y borra el usuario local.
      const hadUser = !!oidcUserRef.current
      setUser(null)
      if (hadUser) {
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
    if (!manager) throw new Error('El proveedor de identidad no está configurado.')
    await manager.signinRedirect()
  }, [manager])

  const completeExternalLogin = useCallback(async () => {
    if (!manager) throw new Error('El proveedor de identidad no está configurado.')
    setUser(await manager.signinRedirectCallback())
  }, [manager, setUser])

  const isAuthenticated =
    config?.mode === 'Local' ? !!localToken
      : config?.mode === 'Jwt' ? (manager ? !!oidcUser : true)
        : false

  return (
    <AuthContext.Provider value={{ config, configError, isAuthenticated, login, loginExternal, completeExternalLogin, logout }}>
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

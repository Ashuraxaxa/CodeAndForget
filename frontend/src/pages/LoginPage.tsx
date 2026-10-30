import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Navigate, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { LogIn } from 'lucide-react'
import { LanguageSelect, useT } from '../i18n'
import { Brand } from '../components/Brand'
import { ThemeToggle } from '../components/ThemeToggle'

export function LoginPage() {
  const { config, configError, isAuthenticated, login, loginExternal } = useAuth()
  const t = useT()
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  if (isAuthenticated) return <Navigate to="/" replace />

  async function run(action: () => Promise<void>) {
    setBusy(true)
    setError(null)
    try {
      await action()
    } catch (err) {
      setError(err instanceof Error ? err.message : t('No se pudo iniciar sesión'))
      setBusy(false)
    }
  }

  function submit(e: FormEvent) {
    e.preventDefault()
    run(() => login(username, password)).then(() => setBusy(false))
  }

  return (
    <div className="login">
      <div className="login-tools"><ThemeToggle /><LanguageSelect /></div>
      <div className="card login-card">
        <div className="login-head">
          <Brand />
          <h1>{t('Bienvenido')}</h1>
        </div>
        {configError && <div className="alert">{t('No se pudo leer la configuración de acceso: {error}', { error: configError })}</div>}
        {!config && !configError && <p className="muted">{t('Cargando…')}</p>}

        {config?.mode === 'Local' && (
          <form className="login-form" onSubmit={submit}>
            <p className="muted">{t('Ingresa para administrar tus tablas.')}</p>
            <label>
              {t('Usuario')}
              <input value={username} onChange={e => setUsername(e.target.value)} autoComplete="username" autoFocus required />
            </label>
            <label>
              {t('Contraseña')}
              <input type="password" value={password} onChange={e => setPassword(e.target.value)} autoComplete="current-password" required />
            </label>
            {error && <div className="alert">{error}</div>}
            <button className="primary block" disabled={busy}><LogIn size={16} aria-hidden />{busy ? t('Ingresando…') : t('Ingresar')}</button>
          </form>
        )}

        {config?.mode === 'Jwt' && config.clientId && (
          <>
            <p className="muted">{t('El acceso se valida con {provider}.', { provider: config.providerName || t('el proveedor de identidad') })}</p>
            {error && <div className="alert">{error}</div>}
            <button className="primary block" disabled={busy} onClick={() => run(loginExternal)}>
              <LogIn size={16} aria-hidden />
              {busy ? t('Redirigiendo…') : t('Ingresar con {provider}', { provider: config.providerName || t('el proveedor de identidad') })}
            </button>
          </>
        )}

        {config?.mode === 'Jwt' && !config.clientId && (
          <div className="alert">
            {t('El acceso lo gestiona el proxy de tu organización y no llegó una sesión válida. Vuelve a ingresar desde el portal.')}
          </div>
        )}
      </div>
    </div>
  )
}

/** Vuelta del proveedor OIDC (redirect_uri): completa el login y entra a la app. */
export function AuthCallbackPage() {
  const { config, completeExternalLogin } = useAuth()
  const t = useT()
  const navigate = useNavigate()
  const [error, setError] = useState<string | null>(null)
  const done = useRef(false)

  useEffect(() => {
    if (!config || done.current) return
    done.current = true
    completeExternalLogin()
      .then(() => navigate('/', { replace: true }))
      .catch(e => setError((e as Error).message))
  }, [config, completeExternalLogin, navigate])

  return (
    <div className="login">
      <div className="card login-card">
        {error ? <div className="alert">{t('No se pudo completar el inicio de sesión: {error}', { error })}</div> : <p className="muted">{t('Completando el inicio de sesión…')}</p>}
      </div>
    </div>
  )
}

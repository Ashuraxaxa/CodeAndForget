import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react'
import { api, setUnauthorizedHandler, tokenStore } from '../api/client'

interface AuthState {
  isAuthenticated: boolean
  login: (username: string, password: string) => Promise<void>
  logout: () => void
}

const AuthContext = createContext<AuthState | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [token, setToken] = useState(() => tokenStore.get())

  const logout = useCallback(() => {
    tokenStore.set(null)
    setToken(null)
  }, [])

  useEffect(() => setUnauthorizedHandler(logout), [logout])

  const login = useCallback(async (username: string, password: string) => {
    const res = await api.login(username, password)
    tokenStore.set(res.token)
    setToken(res.token)
  }, [])

  return (
    <AuthContext.Provider value={{ isAuthenticated: !!token, login, logout }}>
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

import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'
import { apiFetch, clearToken, getToken, setToken, type AuthResponse, type User } from './api'

type AuthContextValue = {
  user: User | null
  loading: boolean
  login: (email: string, password: string, turnstileToken: string) => Promise<void>
  register: (email: string, name: string, password: string, turnstileToken: string) => Promise<void>
  logout: () => void
}

const AuthContext = createContext<AuthContextValue | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null)
  const [loading, setLoading] = useState(true)

  // Rehydrate the session from a stored token on first load.
  useEffect(() => {
    if (!getToken()) {
      setLoading(false)
      return
    }

    apiFetch<User>('/api/auth/me')
      .then(setUser)
      .catch(() => clearToken())
      .finally(() => setLoading(false))
  }, [])

  const value: AuthContextValue = {
    user,
    loading,

    login: async (email, password, turnstileToken) => {
      const response = await apiFetch<AuthResponse>('/api/auth/login', {
        method: 'POST',
        body: JSON.stringify({ email, password, turnstileToken }),
      })

      setToken(response.token)
      setUser(response.user)
    },

    // Registration no longer starts a session -- the account is unusable until the emailed link is followed.
    register: (email, name, password, turnstileToken) =>
      apiFetch<void>('/api/auth/register', {
        method: 'POST',
        body: JSON.stringify({ email, name, password, turnstileToken }),
      }),

    logout: () => {
      clearToken()
      setUser(null)
    },
  }

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth() {
  const context = useContext(AuthContext)

  if (!context) {
    throw new Error('useAuth must be used inside an AuthProvider')
  }

  return context
}

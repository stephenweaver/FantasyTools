import { useEffect, useState } from 'react'
import { apiFetch } from '../lib/api'
import { useAuth } from '../lib/auth'

export default function Home() {
  const { user, logout } = useAuth()
  const [greeting, setGreeting] = useState('…')

  // Proves the stored token is accepted by an [Authorize] endpoint.
  useEffect(() => {
    apiFetch<{ message: string }>('/api/hello/secure')
      .then((r) => setGreeting(r.message))
      .catch((ex: Error) => setGreeting(ex.message))
  }, [])

  return (
    <div className="mx-auto flex min-h-screen max-w-lg flex-col justify-center gap-4 p-6">
      <h1 className="text-3xl font-semibold">{greeting}</h1>
      <p className="text-slate-600">Signed in as {user?.email}</p>

      <button
        className="w-fit rounded border border-slate-300 px-3 py-2"
        type="button"
        onClick={logout}
      >
        Sign out
      </button>
    </div>
  )
}

import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { ApiError } from '../lib/api'
import { useAuth } from '../lib/auth'
import { Turnstile } from '../lib/turnstile'

export default function Login() {
  const { login } = useAuth()
  const navigate = useNavigate()

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [captchaToken, setCaptchaToken] = useState('')
  const [resetSignal, setResetSignal] = useState(0)
  const [error, setError] = useState('')
  const [unverified, setUnverified] = useState(false)
  const [busy, setBusy] = useState(false)

  const submit = async (event: React.FormEvent) => {
    event.preventDefault()
    setError('')
    setUnverified(false)
    setBusy(true)

    try {
      await login(email, password, captchaToken)
      navigate('/')
    } catch (ex) {
      setError((ex as Error).message)

      // 403 means the credentials were right but the address is unconfirmed -- offer a resend.
      // 401 is a plain bad-credentials answer and must not lead anywhere.
      if (ex instanceof ApiError && ex.status === 403) {
        setUnverified(true)
      }

      setResetSignal((n) => n + 1)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="mx-auto flex min-h-screen max-w-sm flex-col justify-center gap-6 p-6">
      <h1 className="text-2xl font-semibold">Sign in</h1>

      <form onSubmit={submit} className="flex flex-col gap-3">
        <input
          className="rounded border border-slate-300 px-3 py-2"
          type="email"
          placeholder="Email"
          autoComplete="username"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          required
        />
        <input
          className="rounded border border-slate-300 px-3 py-2"
          type="password"
          placeholder="Password"
          autoComplete="current-password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          required
        />

        <Turnstile onToken={setCaptchaToken} resetSignal={resetSignal} />

        {error && <p className="text-sm text-red-600">{error}</p>}

        {unverified && (
          <Link className="text-sm underline" to={`/check-email?email=${encodeURIComponent(email)}`}>
            Resend the verification email
          </Link>
        )}

        <button
          className="rounded bg-slate-900 px-3 py-2 text-white disabled:opacity-50"
          type="submit"
          disabled={busy || !captchaToken}
        >
          {busy ? 'Signing in…' : 'Sign in'}
        </button>
      </form>

      <p className="text-sm text-slate-600">
        No account? <Link className="underline" to="/register">Create one</Link>
      </p>
    </div>
  )
}

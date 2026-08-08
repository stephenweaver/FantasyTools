import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useAuth } from '../lib/auth'
import { Turnstile } from '../lib/turnstile'

export default function Register() {
  const { register } = useAuth()
  const navigate = useNavigate()

  const [email, setEmail] = useState('')
  const [name, setName] = useState('')
  const [password, setPassword] = useState('')
  const [captchaToken, setCaptchaToken] = useState('')
  const [resetSignal, setResetSignal] = useState(0)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  const submit = async (event: React.FormEvent) => {
    event.preventDefault()
    setError('')
    setBusy(true)

    try {
      await register(email, name, password, captchaToken)
      navigate(`/check-email?email=${encodeURIComponent(email)}`)
    } catch (ex) {
      setError((ex as Error).message)
      // Turnstile tokens are single-use, so a retry needs a fresh one.
      setResetSignal((n) => n + 1)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="mx-auto flex min-h-screen max-w-sm flex-col justify-center gap-6 p-6">
      <h1 className="text-2xl font-semibold">Create an account</h1>

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
          type="text"
          placeholder="Name"
          autoComplete="name"
          value={name}
          onChange={(e) => setName(e.target.value)}
        />
        <input
          className="rounded border border-slate-300 px-3 py-2"
          type="password"
          placeholder="Password (8+ characters)"
          autoComplete="new-password"
          minLength={8}
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          required
        />

        <Turnstile onToken={setCaptchaToken} resetSignal={resetSignal} />

        {error && <p className="text-sm text-red-600">{error}</p>}

        <button
          className="rounded bg-slate-900 px-3 py-2 text-white disabled:opacity-50"
          type="submit"
          disabled={busy || !captchaToken}
        >
          {busy ? 'Creating…' : 'Create account'}
        </button>
      </form>

      <p className="text-sm text-slate-600">
        Already have one? <Link className="underline" to="/login">Sign in</Link>
      </p>
    </div>
  )
}

import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { AuthShell } from '../lib/AuthShell'
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
    <AuthShell title="Create an account" blurb="Your commissioner connects this account to a Sleeper roster once your email is confirmed.">
      <form onSubmit={submit}>
        <label>
          MANAGER EMAIL
          <input
            type="email"
            placeholder="Email"
            autoComplete="username"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            required
          />
        </label>

        <label>
          DISPLAY NAME
          <input
            type="text"
            placeholder="Name"
            autoComplete="name"
            value={name}
            onChange={(e) => setName(e.target.value)}
          />
        </label>

        <label>
          PASSWORD
          <input
            type="password"
            placeholder="Password (8+ characters)"
            autoComplete="new-password"
            minLength={8}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
          />
        </label>

        <div className="auth-captcha">
          <Turnstile onToken={setCaptchaToken} resetSignal={resetSignal} />
        </div>

        {error && <div className="auth-error">⚠ {error}</div>}

        <button className="primary" type="submit" disabled={busy || !captchaToken}>
          {busy ? 'Creating…' : 'Create account'} <span>→</span>
        </button>
      </form>

      <p className="auth-footer">
        Already have one? <Link to="/login">Sign in</Link>
      </p>
    </AuthShell>
  )
}

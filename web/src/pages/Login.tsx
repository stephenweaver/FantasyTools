import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { ApiError } from '../lib/api'
import { AuthShell } from '../lib/AuthShell'
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
    <AuthShell title="Sign in" blurb="Enter the league room with the account your commissioner connected to your Sleeper roster.">
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
          PASSWORD
          <input
            type="password"
            placeholder="Password"
            autoComplete="current-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
          />
        </label>

        <div className="auth-captcha">
          <Turnstile onToken={setCaptchaToken} resetSignal={resetSignal} />
        </div>

        {error && <div className="auth-error">⚠ {error}</div>}

        {unverified && (
          <Link className="auth-link" to={`/check-email?email=${encodeURIComponent(email)}`}>
            Resend the verification email
          </Link>
        )}

        <button className="primary" type="submit" disabled={busy || !captchaToken}>
          {busy ? 'Signing in…' : 'Sign in'} <span>→</span>
        </button>
      </form>

      <p className="auth-footer">
        No account? <Link to="/register">Create one</Link>
      </p>
    </AuthShell>
  )
}

import { useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { apiFetch } from '../lib/api'
import { AuthShell } from '../lib/AuthShell'
import { Turnstile } from '../lib/turnstile'

export default function CheckEmail() {
  const [params] = useSearchParams()
  const email = params.get('email') ?? ''

  const [captchaToken, setCaptchaToken] = useState('')
  const [resetSignal, setResetSignal] = useState(0)
  const [status, setStatus] = useState('')
  const [busy, setBusy] = useState(false)

  const resend = async () => {
    setStatus('')
    setBusy(true)

    try {
      await apiFetch<void>('/api/auth/resend-verification', {
        method: 'POST',
        body: JSON.stringify({ email, turnstileToken: captchaToken }),
      })

      // The API answers the same way whether or not the account exists, so this message must not
      // claim an email was definitely sent to a real account.
      setStatus('If that account still needs verifying, another email is on its way.')
    } catch (ex) {
      setStatus((ex as Error).message)
    } finally {
      setResetSignal((n) => n + 1)
      setBusy(false)
    }
  }

  return (
    <AuthShell title="Check your inbox">
      <p className="auth-body">
        We sent a verification link to <b>{email || 'your email address'}</b>. Click it to finish setting
        up your account. The link expires in 24 hours.
      </p>

      <div className="auth-resend">
        <span className="eyebrow">DIDN'T GET IT?</span>

        <div className="auth-captcha">
          <Turnstile onToken={setCaptchaToken} resetSignal={resetSignal} />
        </div>

        <button className="secondary" type="button" onClick={resend} disabled={busy || !captchaToken || !email}>
          {busy ? 'Sending…' : 'Resend email'}
        </button>

        {status && <p className="auth-note">{status}</p>}
      </div>

      <p className="auth-footer">
        <Link to="/login">Back to sign in</Link>
      </p>
    </AuthShell>
  )
}

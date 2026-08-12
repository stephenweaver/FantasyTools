import { useEffect, useRef, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { apiFetch } from '../lib/api'
import { AuthShell } from '../lib/AuthShell'

type State = 'working' | 'done' | 'failed'

export default function Verify() {
  const [params] = useSearchParams()
  const email = params.get('email') ?? ''
  const token = params.get('token') ?? ''

  const [state, setState] = useState<State>('working')

  // StrictMode double-invokes effects in dev, which would fire two verify requests for one
  // page load. Guard so the link is redeemed exactly once per mount.
  const sent = useRef(false)

  useEffect(() => {
    if (!email || !token) {
      setState('failed')
      return
    }

    if (sent.current) {
      return
    }

    sent.current = true

    apiFetch<void>('/api/auth/verify', {
      method: 'POST',
      body: JSON.stringify({ email, token }),
    })
      .then(() => setState('done'))
      .catch(() => setState('failed'))
  }, [email, token])

  if (state === 'working') {
    return <div className="auth-loading">Verifying…</div>
  }

  if (state === 'done') {
    return (
      <AuthShell title="Email verified" blurb="Your account is ready. You can sign in now.">
        <Link className="primary" to="/login">Go to sign in <span>→</span></Link>
      </AuthShell>
    )
  }

  return (
    <AuthShell title="That link didn't work" blurb="It may have expired or already been used. Request a new one and try again.">
      <Link className="primary" to={`/check-email?email=${encodeURIComponent(email)}`}>
        Send a new link <span>→</span>
      </Link>

      <p className="auth-footer">
        <Link to="/login">Go to sign in</Link>
      </p>
    </AuthShell>
  )
}

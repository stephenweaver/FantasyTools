import { useEffect, useRef, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { apiFetch } from '../lib/api'

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
    return <div className="p-6 text-slate-600">Verifying…</div>
  }

  return (
    <div className="mx-auto flex min-h-screen max-w-md flex-col justify-center gap-4 p-6">
      {state === 'done' ? (
        <>
          <h1 className="text-2xl font-semibold">Email verified</h1>
          <p className="text-slate-600">Your account is ready. You can sign in now.</p>
        </>
      ) : (
        <>
          <h1 className="text-2xl font-semibold">That link didn't work</h1>
          <p className="text-slate-600">
            It may have expired or already been used. Request a new one and try again.
          </p>
          <Link className="underline" to={`/check-email?email=${encodeURIComponent(email)}`}>
            Send a new link
          </Link>
        </>
      )}

      <Link className="w-fit rounded bg-slate-900 px-3 py-2 text-white" to="/login">
        Go to sign in
      </Link>
    </div>
  )
}

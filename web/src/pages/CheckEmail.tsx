import { useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { apiFetch } from '../lib/api'
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
    <div className="mx-auto flex min-h-screen max-w-md flex-col justify-center gap-4 p-6">
      <h1 className="text-2xl font-semibold">Check your inbox</h1>

      <p className="text-slate-600">
        We sent a verification link to <span className="font-medium">{email || 'your email address'}</span>.
        Click it to finish setting up your account. The link expires in 24 hours.
      </p>

      <div className="flex flex-col gap-3 border-t border-slate-200 pt-4">
        <p className="text-sm text-slate-600">Didn't get it?</p>

        <Turnstile onToken={setCaptchaToken} resetSignal={resetSignal} />

        <button
          className="w-fit rounded border border-slate-300 px-3 py-2 disabled:opacity-50"
          type="button"
          onClick={resend}
          disabled={busy || !captchaToken || !email}
        >
          {busy ? 'Sending…' : 'Resend email'}
        </button>

        {status && <p className="text-sm text-slate-600">{status}</p>}
      </div>

      <p className="text-sm text-slate-600">
        <Link className="underline" to="/login">Back to sign in</Link>
      </p>
    </div>
  )
}

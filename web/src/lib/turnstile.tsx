import { useEffect, useRef, useState } from 'react'
import { getConfig } from './config'

const SCRIPT_URL = 'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit'

type TurnstileApi = {
  render: (el: HTMLElement, options: Record<string, unknown>) => string
  reset: (id: string) => void
  remove: (id: string) => void
}

declare global {
  interface Window {
    turnstile?: TurnstileApi
  }
}

let scriptPromise: Promise<void> | null = null

function loadScript() {
  if (!scriptPromise) {
    scriptPromise = new Promise<void>((resolve, reject) => {
      const script = document.createElement('script')
      script.src = SCRIPT_URL
      script.async = true
      script.onload = () => resolve()
      script.onerror = () => reject(new Error('Could not load the captcha. Check your connection.'))
      document.head.appendChild(script)
    })
  }

  return scriptPromise
}

/**
 * Renders a Cloudflare Turnstile widget and hands the token up.
 *
 * Turnstile tokens are single-use, so the parent form must call the resetSignal prop
 * (by incrementing it) after any failed submit, or the retry sends a stale token.
 */
export function Turnstile({
  onToken,
  resetSignal = 0,
}: {
  onToken: (token: string) => void
  resetSignal?: number
}) {
  const container = useRef<HTMLDivElement>(null)
  const widgetId = useRef<string | null>(null)
  const [error, setError] = useState('')
  const [disabled, setDisabled] = useState(false)

  useEffect(() => {
    let cancelled = false

    const render = async () => {
      try {
        const { captchaEnabled, turnstileSiteKey } = await getConfig()

        // The API reports the captcha off (TURNSTILE_ENABLED=false), so render nothing and let the
        // form submit. The server accepts any token in that mode -- it stays the authority either way.
        if (!captchaEnabled) {
          if (!cancelled) {
            setDisabled(true)
            onToken('captcha-disabled')
          }
          return
        }

        await loadScript()

        if (cancelled || !container.current || !window.turnstile) {
          return
        }

        if (!turnstileSiteKey) {
          setError('Captcha is enabled but TURNSTILE_SITE_KEY is not set.')
          return
        }

        widgetId.current = window.turnstile.render(container.current, {
          sitekey: turnstileSiteKey,
          callback: (token: string) => onToken(token),
          'expired-callback': () => onToken(''),
          'error-callback': () => {
            onToken('')
            setError('Captcha failed to load.')
          },
        })
      } catch (ex) {
        if (!cancelled) {
          setError((ex as Error).message)
        }
      }
    }

    render()

    return () => {
      cancelled = true

      if (widgetId.current && window.turnstile) {
        window.turnstile.remove(widgetId.current)
        widgetId.current = null
      }
    }
    // Rendered once per mount; onToken is intentionally not a dependency.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  useEffect(() => {
    if (resetSignal > 0 && widgetId.current && window.turnstile) {
      onToken('')
      window.turnstile.reset(widgetId.current)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [resetSignal])

  if (disabled) {
    return null
  }

  return (
    <div>
      <div ref={container} data-testid="turnstile" />
      {error && <p className="text-sm text-red-600">{error}</p>}
    </div>
  )
}

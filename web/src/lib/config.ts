import { apiFetch } from './api'

export type AppConfig = {
  captchaEnabled: boolean
  turnstileSiteKey: string
}

let cached: Promise<AppConfig> | null = null

/** Public config from the API, so the root .env stays the single source of truth. */
export function getConfig() {
  if (!cached) {
    cached = apiFetch<AppConfig>('/api/config')
  }

  return cached
}

import { expect, test, type Page } from '@playwright/test'
import { existsSync, readFileSync, rmSync } from 'node:fs'
import { join } from 'node:path'

// Requires `npm run dev` here plus the API on the e2e profile:
//
//   dotnet run --project api/FantasyTools.Api --launch-profile e2e
//
// That profile sets TURNSTILE_ENABLED=false (Turnstile deliberately will not auto-solve for an
// automated browser, so the widget is skipped entirely) and MAIL_TRANSPORT=outbox, so the suite
// needs no Cloudflare connectivity and sends no real mail.
//
// The captcha itself is therefore NOT covered here -- it is verified separately against the live
// siteverify endpoint. What these tests cover is the registration and verification flow.

const password = 'hunter2hunter2'
const outbox = process.env.MAIL_OUTBOX_FOLDER ?? 'C:\\FantasyTools\\Outbox'

const uniqueEmail = () => `e2e-${Date.now()}-${Math.floor(Math.random() * 1e6)}@example.com`

/** Pulls the verification link out of the message the API wrote to the local outbox. */
function readVerificationUrl(email: string) {
  const path = join(outbox, `${email}.txt`)

  expect(existsSync(path), `no verification email was written to ${path}`).toBe(true)

  const match = readFileSync(path, 'utf8').match(/http:\/\/\S*\/verify\?\S+/)

  expect(match, 'the email did not contain a verification link').not.toBeNull()

  return match![0]
}

const clearOutbox = (email: string) => rmSync(join(outbox, `${email}.txt`), { force: true })

/**
 * Submit buttons stay disabled until the captcha hands over a token. With the captcha off that is
 * immediate, but waiting for enabled keeps the tests correct either way.
 */
async function submitWhenCaptchaReady(page: Page, name: string) {
  const button = page.getByRole('button', { name })

  await expect(button).toBeEnabled({ timeout: 20_000 })
  await button.click()
}

async function registerVia(page: Page, email: string, name = 'E2E User') {
  await page.goto('/register')
  await expect(page.getByRole('heading')).toHaveText('Create an account')

  await page.getByPlaceholder('Email').fill(email)
  await page.getByPlaceholder('Name').fill(name)
  await page.getByPlaceholder('Password (8+ characters)').fill(password)

  await submitWhenCaptchaReady(page, 'Create account')
}

test.describe('register, verify, sign in', () => {
  test.describe.configure({ timeout: 60_000 })

  test('the full happy path', async ({ page }) => {
    const email = uniqueEmail()
    clearOutbox(email)

    await registerVia(page, email)

    // Registration no longer starts a session.
    await expect(page).toHaveURL(/\/check-email/)
    await expect(page.getByRole('heading')).toHaveText('Check your inbox')
    expect(await page.evaluate(() => localStorage.getItem('fantasytools.token'))).toBeNull()

    // Signing in before verifying is refused, and the resend route is offered.
    await page.goto('/login')
    await expect(page.getByRole('heading')).toHaveText('Sign in')
    await page.getByPlaceholder('Email').fill(email)
    await page.getByPlaceholder('Password').fill(password)
    await submitWhenCaptchaReady(page, 'Sign in')

    await expect(page.getByText('Please verify your email before signing in.')).toBeVisible()
    await expect(page.getByRole('link', { name: 'Resend the verification email' })).toBeVisible()
    await expect(page).toHaveURL(/\/login$/)

    // Follow the emailed link.
    await page.goto(readVerificationUrl(email))
    await expect(page.getByRole('heading')).toHaveText('Email verified')

    // Following it twice must not look like a broken link.
    await page.goto(readVerificationUrl(email))
    await expect(page.getByRole('heading')).toHaveText('Email verified')

    // Now the account works.
    await page.goto('/login')
    await page.getByPlaceholder('Email').fill(email)
    await page.getByPlaceholder('Password').fill(password)
    await submitWhenCaptchaReady(page, 'Sign in')

    await expect(page.getByRole('heading')).toHaveText('Hello, E2E User')
    await expect(page.getByText(`Signed in as ${email}`)).toBeVisible()

    // The session survives a reload.
    await page.reload()
    await expect(page.getByRole('heading')).toHaveText('Hello, E2E User')

    await page.getByRole('button', { name: 'Sign out' }).click()
    await expect(page).toHaveURL(/\/login$/)
  })

  test('rejects a duplicate email and a bad password', async ({ page }) => {
    const email = uniqueEmail()
    clearOutbox(email)

    await registerVia(page, email)
    await expect(page.getByRole('heading')).toHaveText('Check your inbox')

    await registerVia(page, email)
    await expect(page.getByText('An account with that email already exists.')).toBeVisible()

    await page.goto(readVerificationUrl(email))
    await expect(page.getByRole('heading')).toHaveText('Email verified')

    await page.goto('/login')
    await page.getByPlaceholder('Email').fill(email)
    await page.getByPlaceholder('Password').fill('definitely-not-it')
    await submitWhenCaptchaReady(page, 'Sign in')

    await expect(page.getByText('Invalid email or password.')).toBeVisible()

    // A wrong password must not offer the resend route -- that would leak that the account exists.
    await expect(page.getByRole('link', { name: 'Resend the verification email' })).toHaveCount(0)
  })

  test('a tampered verification link is refused', async ({ page }) => {
    const email = uniqueEmail()
    clearOutbox(email)

    await registerVia(page, email)
    await expect(page.getByRole('heading')).toHaveText('Check your inbox')

    await page.goto(`${readVerificationUrl(email)}tampered`)
    await expect(page.getByRole('heading')).toHaveText("That link didn't work")

    // ...and the account is still locked out.
    await page.goto('/login')
    await page.getByPlaceholder('Email').fill(email)
    await page.getByPlaceholder('Password').fill(password)
    await submitWhenCaptchaReady(page, 'Sign in')

    await expect(page.getByText('Please verify your email before signing in.')).toBeVisible()
  })
})

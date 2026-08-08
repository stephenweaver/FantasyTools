# FantasyTools

.NET 9 API + React front end. Accounts are stored as documents through
[`StephenWeaver.Common`](https://github.com/stephenweaver/StockScreener/pkgs/nuget/StephenWeaver.Common) —
local disk in dev, Cloudflare R2 in prod.

```
api/FantasyTools.Api    ASP.NET Core API (controllers, JWT auth)
web/                    Vite + React + TypeScript + Tailwind 4
```

## Prerequisites

- .NET 9 SDK
- Node 20+
- A classic GitHub PAT with `read:packages`, exported as `GITHUB_PACKAGES_TOKEN`

`nuget.config` points at `https://nuget.pkg.github.com/stephenweaver/index.json` and expands
`%GITHUB_PACKAGES_TOKEN%` from the **process environment** — GitHub Packages requires auth even for
public packages, and `.env` is not read during restore.

## Run it

```powershell
copy .env.example .env      # then edit JWT_SECRET and the TURNSTILE_* keys

# terminal 1 — API on http://localhost:5080
dotnet run --project api\FantasyTools.Api

# terminal 2 — UI on http://localhost:5173
cd web
npm install
npm run dev
```

Vite proxies `/api` to the API, so the browser stays on one origin — no CORS, no API base URL to configure.

## Endpoints

| Method | Route | Auth | |
|---|---|---|---|
| GET | `/api/config` | anon | `{captchaEnabled, turnstileSiteKey}` for the front end |
| GET | `/api/version` | anon | `{gitSha}` — the commit this image was built from, `unknown` outside a container |
| GET | `/api/hello` | anon | `{ message: "Hello, world" }` |
| GET | `/api/hello/secure` | bearer | `{ message: "Hello, {name}" }` |
| POST | `/api/auth/register` | anon | `{email,name,password,turnstileToken}` → **204, no token** |
| POST | `/api/auth/login` | anon | `{email,password,turnstileToken}` → `{token, user}` |
| POST | `/api/auth/verify` | anon | `{email,token}` → 204, or 400 for a bad/expired link |
| POST | `/api/auth/resend-verification` | anon | `{email,turnstileToken}` → always 204 |
| GET | `/api/auth/me` | bearer | `{userId, email, name, emailVerified}` |

Auth is deliberately minimal: a `UserDocument` holding a `PasswordHasher<T>` (PBKDF2-SHA256) hash, and a
7-day HS256 JWT signed with `JWT_SECRET`. No refresh tokens, no server-side sessions.

Three response codes carry meaning and must not be collapsed into each other:

- **401** on login — wrong password, or no such account. Deliberately the same answer for both.
- **403** on login — password was right but the address is unverified. The UI keys off this to offer a resend.
- **400** on verify — bad token, expired token, or unknown account. Identical for all three; since the
  token is unguessable, a failure reveals nothing about whether the address is registered.

`resend-verification` always answers 204 so it cannot be used to enumerate accounts, and is throttled to
one email per account per 60 seconds.

## Email verification

Registration creates the account but issues **no session** — login returns 403 until the emailed link is
followed. The token is 32 random bytes; only its SHA-256 hash is persisted, since the token itself is a
bearer credential sitting in an inbox. Links expire after 24 hours and are idempotent, so a
double-clicked or prefetched link succeeds twice rather than reporting itself broken.

Mail goes through `IMailerSendService` from the Common package. `MAIL_TRANSPORT` picks the behaviour:

| Value | Effect |
|---|---|
| `mailersend` | Sends for real. The default when `MAILERSEND_API_KEY` is set. |
| `outbox` | Writes the rendered message to `MAIL_OUTBOX_FOLDER` instead. The default with no API key. |
| `both` | Sends and keeps a local copy. |

`outbox` is what lets a fresh clone complete the whole verification loop with no mail credentials, and
it is what the e2e suite reads to get the link.

## Captcha

Cloudflare Turnstile gates register, login, and resend. The site key is served from `/api/config` so the
root `.env` stays the single source of truth and the web app needs no build-time environment of its own.
Tokens are single-use, so each form resets its widget after a failed submit.

Set `TURNSTILE_ENABLED=false` to switch it off entirely — the server accepts any token and the widget is
not rendered. Only the literal string `false` disables it, so a missing or misspelled variable leaves the
captcha on, and startup logs a loud warning whenever it is off.

> Turnstile will not auto-solve for an automated browser — it runs browser-integrity fingerprinting and
> simply never issues a token. That is the captcha working, not a misconfiguration. The e2e suite
> therefore runs with it disabled.

## Storage

`UserDocument.Id` is the normalized email and `Pk` is `users`, so a login is a single
`IFileService.Retrieve` — there is no query engine behind the document store. Locally that lands at
`C:\FantasyTools\Documents\users\<email>.json`; in R2 it's the object key `users/<email>.json`.

Switching to R2 is env-only — there is no storage code in this repo at all:

```
FILE_SERVICE=R2
R2_CONNECTION_STRING=https://<account>.r2.cloudflarestorage.com
R2_ACCESS_KEY=...
R2_SECRET_KEY=...
R2_BUCKET=fantasytools
```

Env vars follow the StockWatch convention: the plain name is the prod value and `{NAME}_LOCAL` wins in
Debug builds, so `FILE_SERVICE=R2` + `FILE_SERVICE_LOCAL=local` means prod uses R2 while a local debug
run writes to disk. `DOCUMENTS_FOLDER` / `DOCUMENTS_FOLDER_LOCAL` set the root folder the same way.

> Requires **StephenWeaver.Common 1.1.0+**. In 1.0.0 the local folder was hardcoded to
> `C:\StockWatch\Documents`, the `_LOCAL` convention was compiled out of the Release-built package, and an
> empty `R2_CONNECTION_STRING=` crashed startup with `No RegionEndpoint or ServiceURL configured`.

## Deploying

Pushing a `vX.Y.Z` tag builds and pushes two images to GHCR:

| Image | From | Serves |
|---|---|---|
| `ghcr.io/stephenweaver/fantasytools-api` | `api/Dockerfile` | ASP.NET on :8080, `/health` for probes |
| `ghcr.io/stephenweaver/fantasytools-web` | `web/Dockerfile` | nginx on :8080 — static `dist` plus an `/api` proxy |

```powershell
git tag v1.0.0        # -> :v1.0.0 and :latest
git tag v1.1.0-beta.1 # -> :v1.1.0-beta.1 and :beta-latest, leaves :latest alone
git push --tags
```

### Which build is running

The workflow passes `--build-arg GIT_SHA=${{ github.sha }}` to both images and each exposes its own:

```bash
curl https://fantasytool.stephenweaver.dev/version      # nginx, from /version.txt baked into the image
curl https://fantasytool.stephenweaver.dev/api/version  # the API, {"gitSha":"..."}
```

Ask both. `latest` and `beta-latest` are reassigned on every release, so the tag a container was
pulled by does not identify the code inside it — and since the two images are pulled independently, a
half-finished deploy leaves them on different commits with nothing else to show for it. Locally there
is no `GIT_SHA`, so the API answers `unknown` and the nginx route only exists in the built image.

`workflow_dispatch` builds both without pushing, which is how you check a Dockerfile before cutting a
tag. The repo needs one secret, **`NUGET_PACKAGES_TOKEN`** — a classic PAT with `read:packages`, the
same one restore uses locally. It is deliberately *not* called `GITHUB_PACKAGES_TOKEN` to match the
env var: Actions reserves the `GITHUB_` prefix and refuses to create a secret using it, and
`${{ secrets.GITHUB_PACKAGES_TOKEN }}` would then expand to an empty string and mount nothing. The
name only changes on the GitHub side — the env var restore reads is still `GITHUB_PACKAGES_TOKEN`.
The built-in `GITHUB_TOKEN` cannot stand in either: `StephenWeaver.Common` is
published from the StockScreener repo, and that token only reaches packages owned by this one. It is
mounted as a BuildKit secret, never an `ARG`, so it stays out of the image history.

> Both images build with the **repo root** as context — the API restore needs the root `nuget.config`.
> That is why the workflow passes `context: .` with an explicit `file:` instead of finding Dockerfiles.

### Hosting

`docker-compose-prod.yml` joins the existing external `traefik-public` network, so the Traefik already
running for StockScreener routes it and nothing in that stack changes:

```bash
cp .env.prod.example .env.prod   # fill it in
docker compose -f docker-compose-prod.yml --env-file .env.prod up -d
```

| Host | Goes to |
|---|---|
| `fantasytool.stephenweaver.dev` | nginx — the app, and `/api/*` proxied to the api container |
| `api.fantasytool.stephenweaver.dev` | the API directly |

Both must resolve to whatever fronts Traefik's `web` entrypoint (`:4322`). The browser only ever talks
to the first one: nginx proxies `/api` in prod exactly like Vite does in dev, so `web/src/lib/api.ts`
keeps using relative paths and there is no CORS and no API base URL to build in. The `api.` host is
there for direct/non-browser callers.

Sharing that network with a live stack means three names have to stay unique, and every one of them
fails silently rather than loudly:

- **Router names** are prefixed `fantasytools-`. A duplicate router name across compose projects
  steals the other app's traffic.
- **The compose project** is pinned with `name: fantasytools` instead of being inherited from the
  directory. StockScreener deploys as project `stephen` and already owns containers named
  `stephen-api-N`; landing in that project would collide with them.
- **The api service's network alias** is `fantasytools-api`, and `web/nginx.conf` proxies to that,
  not to `api`. Compose aliases every service by its own name, StockScreener has an `api` service on
  the same network, and Docker DNS returns *every* container answering to a name and round-robins
  between them — so `http://api:8080` would have sent a share of this app's `/api` traffic into
  StockScreener's API. Renaming the alias means editing `nginx.conf` to match.

### The FF_ prefix

Every variable in `.env.prod` is `FF_`-prefixed. **The mapping to the plain names the app reads lives
in `docker-compose-prod.yml` and nowhere else** — the API, the images, and `.env` know nothing about
it. Adding a variable means adding both halves of one line there:

```yaml
- JWT_SECRET=${FF_JWT_SECRET}
```

Two things that do not carry over from local dev:

- **No `{NAME}_LOCAL`.** That convention only fires on Debug builds and the images are Release, so a
  `_LOCAL` variable in production is inert.
- **`TURNSTILE_ENABLED` is deliberately unmapped.** Only the literal `false` disables the captcha, so
  leaving it unset is the safe default and there is no way to typo it off in prod.

## Tests

The suite needs the Vite dev server plus the API on the **`e2e` launch profile**, which turns the captcha
off and forces mail to the outbox:

```powershell
# terminal 1
dotnet run --project api\FantasyTools.Api --launch-profile e2e

# terminal 2
cd web
npm run dev

# terminal 3
cd web
npx playwright test
```

Covers register → blocked login → follow the emailed link → sign in → reload → sign out, plus duplicate
email, bad password, a tampered verification link, and that a wrong password never offers the resend
route (which would leak that the account exists).

The captcha itself is not covered by these tests, for the reason above.

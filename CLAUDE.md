# FantasyTools — agent notes

.NET 9 API + React front end. Auth stub: register → verify email → sign in.
Everything persists as documents through the `StephenWeaver.Common` NuGet package.
Read `README.md` first for the endpoint list and env vars — this file only covers what bites.

```
api/FantasyTools.Api    ASP.NET Core API
web/                    Vite + React 19 + TS + Tailwind 4 + Playwright
```

## Build landmines

**Restore needs `GITHUB_PACKAGES_TOKEN` as a real process environment variable** (classic PAT,
`read:packages`). `nuget.config` expands `%GITHUB_PACKAGES_TOKEN%` from the environment — it does
**not** read `.env`. GitHub Packages requires auth even for public packages.

**Stop the API before rebuilding.** A running `FantasyTools.Api.exe` locks the output and the build
fails with MSB3027 after ten retries.

## Deploy landmines

`README.md` has the mechanics. What bites:

**Both images build with the repo root as context**, not their own folder — the API restore needs the
root `nuget.config`. A Dockerfile that says `COPY package.json` instead of `COPY web/package.json` will
look correct and fail.

**The restore inside Docker needs the PAT as a BuildKit secret**, id `github_packages_token`, because
`nuget.config` expands `%GITHUB_PACKAGES_TOKEN%` from the environment. Never switch it to an `ARG` or a
`COPY` — both leave the token in the image history. CI reads it from the repo secret
**`NUGET_PACKAGES_TOKEN`** — named that because Actions reserves the `GITHUB_` prefix and will not
create a secret using it, so `secrets.GITHUB_PACKAGES_TOKEN` expands to `""`, buildx mounts nothing,
and the build dies on NuGet's `Parameter 'password'` as if the token were bad rather than absent. Only
the GitHub-side name differs; the env var `nuget.config` expands is still `GITHUB_PACKAGES_TOKEN`. The
built-in `GITHUB_TOKEN` cannot reach a package owned by StockScreener.

**The web healthcheck must use `127.0.0.1`, never `localhost`.** busybox wget resolves `localhost` to
`::1` and will not fall back to IPv4; nginx is bound IPv4-only because the image's
`10-listen-on-ipv6-by-default.sh` patches only the config it ships and skips ours the moment it
differs (the container log says so on every boot). The result is a container that is permanently
`unhealthy` while serving every request perfectly — and Traefik appears to withhold the router for
it, so the site 404s with correct labels, correct network, and correct env. Nothing points at the
healthcheck. The api healthcheck is immune only because that image installs real curl, which does
fall back to IPv4.

**`GIT_SHA` comes from the image, never from compose.** Both images take it as a build arg and expose
it — the API reads the ENV at `/api/version`, the web image bakes it into `/usr/share/nginx/html/version.txt`
at build and nginx serves that at `/version` (nginx cannot expand env vars in a config). Mapping a
`FF_GIT_SHA` into `docker-compose-prod.yml` would let a container claim a commit it was not built from,
which is the one thing these routes exist to rule out.

**`FF_` is a compose-level rename, not an app concept.** Prod env vars are `FF_`-prefixed and
`docker-compose-prod.yml` maps each onto the plain name. Do not teach the API, `EnvironmentHelper`, or
`.env` about the prefix — a new prod variable is one line in the compose file. Note that
`StephenWeaver.Common` reads `FILE_SERVICE`, `R2_*` and `DOCUMENTS_FOLDER` through its own `GetVar`
calls, so any scheme that prefixed only this repo's reads would miss them anyway.

**The `traefik-public` network is shared with a live StockScreener stack**, which runs as compose
project `stephen` with services including `api`. Three namespaces overlap there, and all three fail
quietly:

- Router names stay `fantasytools-`-prefixed — a collision hijacks the other app's traffic.
- The compose project is pinned via top-level `name: fantasytools`, not inherited from the directory,
  or `docker compose up` from `/home/stephen` would try to adopt `stephen-api-N`.
- The api service carries the network alias **`fantasytools-api`**, and that — not `api` — is what
  `web/nginx.conf` proxies to. Compose aliases each service by its own name and Docker DNS
  round-robins across every container answering to a name, so `http://api:8080` resolves to
  StockScreener's api replicas as readily as to this one. Verified: two containers sharing an alias,
  `nslookup` returns both. Change one of the two and you must change the other.

**nginx proxies `/api` to the api container** so the front end keeps using relative paths in prod. The
`proxy_pass` goes through a variable plus `resolver 127.0.0.11` on purpose — with a literal hostname
nginx resolves at startup, refuses to boot when the api container is not up yet, and never notices its
address changing on redeploy.

**Traefik on the VPS is v3.3.5 against Docker Engine 28.1.0 (`min_api=1.24`)** — fine as it stands.
But v3.3.5's docker provider cannot talk to Engine 29: it fails with a bare
`Error response from daemon:` and discovers zero containers, so every route 404s while the daemon
looks like the culprit. Upgrading the host's Docker means bumping Traefik first.

`DOCUMENTS_FOLDER` in prod is an R2 key prefix on a Linux container. A Windows path there is taken
literally. **`IMAGES_FOLDER` is the same kind of value** and has the same trap.

**Card artwork lives in a second R2 bucket** (`IMAGES_BUCKET`), public-read, reached by its own
`AmazonS3Client` in `ImageStorageService`. It cannot go through `IFileService` — that is constructed
against `R2_BUCKET` alone and only round-trips `BaseDocument` as JSON. The `R2_*` credentials are
shared, so a new key pair has to be granted on both buckets or uploads start failing while documents
keep working. `IMAGES_BASE_URL` is the public host bound to that bucket and is baked into every stored
card: change the host and existing cards point at the old one, because the URL is persisted, not built
at read time.

**Local artwork is served by static file middleware, not a controller.** `Startup` mounts
`IImageStorageService.LocalRoot` on the request path `/api/images`, and there is deliberately no read
action on `ImagesController`. Two things depend on the `/api` prefix: the Vite proxy in dev and the
nginx `/api` proxy in prod both forward on that prefix alone, so moving the mount to `/images` means
editing both or the images 404 while uploads keep succeeding. Do not reintroduce a read endpoint —
turning a route parameter back into a file path is exactly the code the middleware exists to avoid.

**`web/nginx.conf` must keep `client_max_body_size` above the API's upload cap.** nginx defaults to 1 MB
and rejects larger uploads itself, before the request reaches ASP.NET — the browser gets nginx's HTML
error page instead of the API's message, which reads like the endpoint is broken rather than the file
being too big. Nothing in dev catches this: Vite's proxy has no such limit.

## Mail is Resend, not the Common package's MailerSend

`ResendHttpClient` lives here; `RegisterStephenWeaverCommon` still wires up `IMailerSendService`
because that package is shared with StockScreener, but nothing in this repo resolves it. Do not "tidy
up" by routing mail back through it, and do not delete it from the package.

The send does **not** use `HttpClientBase.Post`. Resend reports a rejected message as a 4xx whose body
carries the reason, and `EnsureSuccessStatusCode` discards that body — leaving "one or more errors
occurred" as the only trace of a sender domain that was never verified.

`MAIL_FROM_EMAIL` must be on a domain verified in the Resend dashboard. That check happens at send
time, so a bad value is a 403 on registration only — startup, `/health`, and every other route stay
green. `MAIL_TRANSPORT=outbox` sidesteps it entirely, which is what the e2e profile does.

## The Common package lives in another repo

Source of truth is `C:\git\StockScreener\api\StephenWeaver.Common`. Changing package behaviour means
editing that repo, bumping `<Version>`, and pushing to `main` — the `publish-common.yml` workflow packs
and pushes to GitHub Packages. Do not vendor or fork it here.

This project needs **1.1.0+** (currently the latest published). 1.0.0 hardcoded the local document
folder, compiled out the `_LOCAL` convention, and crashed on an empty `R2_CONNECTION_STRING=`.

Note: `StockScreener.Common.Tests` has a **pre-existing** build failure (`QueryUtilityTests` calls the
instance method `QueryUtility.ParseCondition` statically). Unrelated to anything here — don't try to
fix it as collateral.

## Document store has no locking

`IFileService` is plain files (or R2 objects). There is no transaction, no optimistic concurrency, and
no row locking. Two consequences that have already caused real bugs:

- **Every read-modify-write of a user document must go through `AuthService.WithUserLock(email, …)`.**
  Without it, concurrent requests collide on the write, and a read landing mid-write returns truncated
  JSON. New mutating endpoints need this too.
- **`FileService.Retrieve` swallows deserialize failures and returns `null`.** So `null` means "missing
  **or** corrupt". Never treat `null` as proof an account doesn't exist in a security decision.

Documents are addressed by `Id`/`Pk` only — there is no query engine. `UserDocument.Id` is the
normalized email, which is what makes login a single `Retrieve`. Any new lookup path needs its own
`Id`-addressable document.

## Turnstile will not work under automation

Turnstile runs browser-integrity fingerprinting and simply never issues a token to Playwright, headless
or headed. That is the captcha working correctly, not a misconfiguration — do not "fix" it, and do not
burn time debugging the widget in a test.

Run the API on the **`e2e` launch profile** for tests, which sets `TURNSTILE_ENABLED=false` and
`MAIL_TRANSPORT=outbox`:

```powershell
dotnet run --project api\FantasyTools.Api --launch-profile e2e   # terminal 1
cd web; npm run dev                                              # terminal 2
cd web; npx playwright test --repeat-each=3                      # terminal 3
```

Only the literal string `false` disables the captcha, so a typo leaves it on. Startup logs a loud
warning whenever it is off.

**Always run e2e with `--repeat-each=3` or more.** Both concurrency bugs found so far passed a single
run and failed on repeat.

## Env var conventions

`Program.cs` loads `.env` with **`Env.NoClobber()`**, so a real environment variable beats the file.
That is what lets launch profiles override config without editing `.env`. (StockScreener clobbers —
this differs deliberately.)

`EnvironmentHelper.GetVar` prefers `{NAME}_LOCAL` in Debug builds. As of 1.1.0 that works for package
consumers; in 1.0.0 it was compiled out because CI packs in Release.

Leave `R2_*` blank locally. `FILE_SERVICE=R2` with an empty connection string fails at startup with a
clear message as of 1.1.0; in 1.0.0 it crashed with `No RegionEndpoint or ServiceURL configured`.

## Auth semantics that must not be collapsed

- **401** on login — wrong password *or* unknown account. Same answer for both, deliberately.
- **403** on login — correct password, unverified address. The UI keys off this to offer a resend.
- **400** on verify — bad, expired, or unknown. Identical for all three.
- **503** on register — account created, mail could not be sent. Not a 500: the retry is the recovery.
- `resend-verification` always returns 204, throttled to one per account per 60s.

**`resend-verification` must keep returning 204 when the send fails.** It is the one place a delivery
failure cannot be reported, because a send is only attempted there for an address that has an unverified
account — so a 503 would answer the question the flat 204 exists to refuse. `AuthController` swallows
`EmailDeliveryException` there and logs it. Register is free to return 503 because it attempts a send in
every non-duplicate case.

**Re-registering an unverified address re-sends its link rather than reporting a duplicate**, but only
when the supplied password already matches, and it never replaces the stored hash. Both rules are load
bearing: without the first this sends mail at strangers' pending addresses; without the second, someone
can re-register another person's pending address and own the account the moment the real owner clicks
the link in their own inbox. The link is not the credential.

Verification tokens are stored **hashed only**. Links are idempotent on purpose — mail clients prefetch
them and users double-click. Never make a valid link fail on second use.

**`IssueVerification` writes the document before sending and rolls it back by hand if the send throws.**
The write has to come first or a link followed from a fast inbox beats its own token to storage. But
leaving the write in place on failure arms the 60s throttle for a message that never left — so the retry
that is meant to recover answers "check your inbox" with nothing behind it — and replaces a working link
from an earlier successful send with one nobody received. There is no transaction; the caller holds the
account lock, which is what makes the second write safe.

## The app has no signed-out state of its own

`App.tsx` renders only behind the gate in `main.tsx`, which redirects to `/login` whenever `user` is
null — **including in dev**. It used to exempt `import.meta.env.DEV`, and the app carried a second,
decorative login screen with the demo credentials prefilled that simply called `setScreen('room')`. The
combination meant a local run looked signed in while holding no token, so every `[Authorize]` call
quietly failed into `localStorage` and the UI reported success. Do not reintroduce either half.

Consequently there is no `getToken()` guard around API calls in `App.tsx`, and there should not be:
inside the app a token always exists, and a guard there converts an expired session into silent
local-only writes instead of a visible error.

All four signed-out pages share `lib/AuthShell.tsx` and are styled by the hand-written CSS in
`index.css`. **Tailwind is installed but `index.css` never imports it**, so any `className="rounded
border …"` is inert — that is what left the original auth pages unstyled. Style with the existing
classes, or import Tailwind deliberately (its preflight will fight the existing CSS).

The wordmark in `AuthShell` is a `div`, not an `h1`, so each page owns exactly one heading. The e2e
suite addresses every auth page through `getByRole('heading')`, which is strict — a second heading
anywhere in that shell fails the run on a match count, not on the text.

## Testing reality

There are no .NET unit tests. Playwright in `web/e2e` is the only automated coverage, and it does not
cover the captcha (see above). Verify API changes with curl against a running instance and by reading
the document at `C:\FantasyTools\Documents\users\<email>.json`; verification emails land in
`C:\FantasyTools\Outbox\<email>.txt` when `MAIL_TRANSPORT=outbox`.

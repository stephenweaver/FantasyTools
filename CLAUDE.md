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
`GITHUB_PACKAGES_TOKEN`; the built-in `GITHUB_TOKEN` cannot reach a package owned by StockScreener.

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
literally.

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
- `resend-verification` always returns 204, throttled to one per account per 60s.

Verification tokens are stored **hashed only**. Links are idempotent on purpose — mail clients prefetch
them and users double-click. Never make a valid link fail on second use.

## Testing reality

There are no .NET unit tests. Playwright in `web/e2e` is the only automated coverage, and it does not
cover the captcha (see above). Verify API changes with curl against a running instance and by reading
the document at `C:\FantasyTools\Documents\users\<email>.json`; verification emails land in
`C:\FantasyTools\Outbox\<email>.txt` when `MAIL_TRANSPORT=outbox`.

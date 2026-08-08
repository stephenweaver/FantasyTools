# Build context is the REPO ROOT, not this folder -- restore needs the root nuget.config.
# See .github/workflows/build-and-push.yml.

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY nuget.config ./
COPY api/FantasyTools.Api/FantasyTools.Api.csproj api/FantasyTools.Api/

# StephenWeaver.Common lives in GitHub Packages, which requires auth even though it is public.
# nuget.config expands %GITHUB_PACKAGES_TOKEN% from the process environment at restore time.
# The PAT is mounted rather than passed as ARG or COPY: either of those would leave it readable
# in the image history forever.
#
# The guard is here because an absent secret is otherwise invisible: buildx mounts nothing, `cat`
# writes to stderr but does not fail the shell, and the build dies further down on NuGet's
# "Value cannot be null or empty string. (Parameter 'password')", which reads like a bad token
# rather than a missing one.
RUN --mount=type=secret,id=github_packages_token \
    if [ ! -s /run/secrets/github_packages_token ]; then \
      echo "ERROR: BuildKit secret 'github_packages_token' is missing or empty." >&2; \
      echo "  CI:    set the repo secret NUGET_PACKAGES_TOKEN (classic PAT, read:packages)." >&2; \
      echo "         It cannot be called GITHUB_PACKAGES_TOKEN -- Actions reserves that prefix." >&2; \
      echo "  Local: docker build --secret id=github_packages_token,env=GITHUB_PACKAGES_TOKEN ..." >&2; \
      exit 1; \
    fi; \
    GITHUB_PACKAGES_TOKEN="$(cat /run/secrets/github_packages_token)" \
    dotnet restore api/FantasyTools.Api/FantasyTools.Api.csproj

COPY api/ api/
RUN dotnet publish api/FantasyTools.Api/FantasyTools.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# The aspnet image ships without curl; the compose healthcheck needs it.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

ARG GIT_SHA
ENV GIT_SHA=${GIT_SHA}

# Release build, so EnvironmentHelper.IsDebugBuild is false and {NAME}_LOCAL is ignored here --
# a stray *_LOCAL leaking into prod config does nothing, which is the point of the convention.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "FantasyTools.Api.dll"]

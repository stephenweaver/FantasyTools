# Build context is the REPO ROOT, not this folder -- matches api/Dockerfile so both images build
# the same way. See .github/workflows/build-and-push.yml.

FROM node:20-alpine AS build
WORKDIR /app

COPY web/package.json web/package-lock.json ./
# Full install, not --omit=dev: the build itself is `tsc -b && vite build`.
RUN npm ci

COPY web/ ./
RUN npm run build

FROM nginx:alpine AS final

COPY web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html

ARG GIT_SHA
ENV GIT_SHA=${GIT_SHA}

# Served at /version (see nginx.conf). Written to a file rather than read from the environment at
# request time because nginx does not expand env vars in a config -- the templating alternative runs
# envsubst over the whole file and would eat $uri, $host and $request_uri along with it. The value is
# fixed when the image is built regardless, so a file loses nothing.
RUN echo "${GIT_SHA:-unknown}" > /usr/share/nginx/html/version.txt

EXPOSE 8080

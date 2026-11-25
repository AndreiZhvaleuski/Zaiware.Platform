# syntax=docker/dockerfile:1
# Install the pinned CLI/server binary with OpenCode's official v2 installer:
# https://opencode.ai/docs/
#
# The installer detects the build platform and libc. The sbx shell workload is
# Debian/glibc, so do not substitute the Alpine/musl binary from the upstream
# ghcr.io/anomalyco/opencode runtime image.
FROM debian:trixie-slim AS build

ARG OPENCODE_VERSION=2.4.1

# Build-only. The installer chooses its default ~/.opencode/bin path from HOME.
ENV HOME=/home/agent

RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates curl tar gzip \
 && rm -rf /var/lib/apt/lists/*

RUN curl -fsSL https://opencode.ai/v2/install -o /tmp/opencode-install.sh \
 && bash /tmp/opencode-install.sh --version "$OPENCODE_VERSION" --no-modify-path \
 && test "$(/home/agent/.opencode/bin/opencode --version)" = "opencode v${OPENCODE_VERSION#v}"

# Add the binary at OpenCode's standard install location and expose it on PATH.
# OpenCode's data volume is elsewhere and untouched.
RUN mkdir -p /out/usr/local/bin /out/home/agent/.opencode/bin \
 && cp -a /home/agent/.opencode/bin/opencode /out/home/agent/.opencode/bin/ \
 && ln -s /home/agent/.opencode/bin/opencode /out/usr/local/bin/opencode \
 && chown -R 1000:1000 /out/home/agent

FROM scratch
COPY --from=build /out /

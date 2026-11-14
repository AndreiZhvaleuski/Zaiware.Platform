# syntax=docker/dockerfile:1
# Build stage installs the standalone OpenSpec binary:
# https://openspec.dev/docs/installation
#
# No Node.js is involved: each release ships a native binary per platform, and
# the installer picks it from the build architecture. It also downloads the
# release's SHA256SUMS and refuses to install on a mismatch, so the Kit does not
# hand-roll the asset name or the checksum.
#
# BuildKit resolves FROM to the *target* platform, so the binary matches the
# sandbox architecture, at the cost of emulation when cross-building.
FROM debian:trixie-slim AS build

ARG OPENSPEC_VERSION=2.0.0

# Build-only. Docker builds run as root with HOME=/root; the installer chooses
# its default path from HOME. Do not set this at runtime.
ENV HOME=/home/agent

RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates curl tar gzip \
 && rm -rf /var/lib/apt/lists/*

# Install at the official default, $HOME/.local/bin. The installer stages its
# download inside that directory, so the build runs as root and the file is
# chowned afterward. The release archive ships uid/gid 1001.
RUN curl -fsSL https://openspec.dev/cli/install.sh -o /tmp/openspec-install.sh \
 && OPENSPEC_VERSION="$OPENSPEC_VERSION" sh /tmp/openspec-install.sh \
 && /home/agent/.local/bin/openspec --version \
 && chown 1000:1000 /home/agent/.local/bin/openspec

# Expose the binary through /usr/local/bin. Keep ~/.local/bin root-owned so the
# agent cannot shadow other tools there; only the binary is uid 1000.
RUN mkdir -p /out/usr/local/bin /out/home/agent/.local/bin \
 && cp -a /home/agent/.local/bin/openspec /out/home/agent/.local/bin/ \
 && ln -s /home/agent/.local/bin/openspec /out/usr/local/bin/openspec \
 && chown 1000:1000 /out/home/agent /out/home/agent/.local \
 && chown 1000:1000 /out/home/agent/.local/bin/openspec

# The overlay. Everything outside /out is the workload's, untouched.
FROM scratch
COPY --from=build /out /

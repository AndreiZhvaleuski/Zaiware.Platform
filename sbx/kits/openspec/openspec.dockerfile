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

RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates curl tar gzip \
 && rm -rf /var/lib/apt/lists/*

# Install into ~/.local/bin: the OpenCode workload already puts that directory
# on PATH, so no symlink or /usr/local/bin copy is needed. The script stages the
# download inside the target directory (so the swap is atomic), which is why it
# must exist and be writable here. --version pins the release.
RUN curl -fsSL https://openspec.dev/cli/install.sh -o /tmp/openspec-install.sh \
 && OPENSPEC_VERSION="$OPENSPEC_VERSION" \
    OPENSPEC_INSTALL_DIR=/home/agent/.local/bin \
      sh /tmp/openspec-install.sh \
 && /home/agent/.local/bin/openspec --version \
 && chown -R 1000:1000 /home/agent/.local/bin

# The overlay. Everything outside /out is the workload's, untouched.
FROM scratch
COPY --from=build /home/agent/.local/bin /home/agent/.local/bin

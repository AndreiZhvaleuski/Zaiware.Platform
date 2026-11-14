# syntax=docker/dockerfile:1
# Build stage downloads the Aspire CLI with Aspire's official install script:
# https://aspire.dev/get-started/install-cli/
#
# The script selects the release archive for the build architecture and
# verifies its SHA-512 before extracting, so the Kit does not hand-roll the
# download URL or the checksum. Pin `ASPIRE_VERSION`; the AppHost is pinned to
# the same Aspire release family in Zaiware.Platform.AppHost.csproj.
FROM debian:trixie-slim AS build

ARG ASPIRE_VERSION=13.6.4

# libicu76 is for the sanity check: the NativeAOT CLI needs ICU to load its
# resources. The sandbox gets ICU from the dotnet Kit, which this mixin
# requires, so do not copy those libraries into the overlay.
RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates curl tar gzip libicu76 \
 && rm -rf /var/lib/apt/lists/*

# Install into ~/.local/bin: the OpenCode workload already puts that directory
# on PATH, so no symlink or /usr/local/bin copy is needed. The CLI derives its
# log, state, and bundle directories from the install root (~/.local), which is
# owned by the agent and stays writable.
#
# --skip-path is required: the script otherwise edits a shell profile we do not
# ship. --version pins the release; --quality is only for "latest of a channel"
# and the script rejects the two together.
RUN curl -fsSL https://aspire.dev/install.sh -o /tmp/aspire-install.sh \
 && bash /tmp/aspire-install.sh \
      --install-path /home/agent/.local/bin \
      --version "$ASPIRE_VERSION" \
      --skip-path \
 && /home/agent/.local/bin/aspire --version \
 && chown -R 1000:1000 /home/agent/.local/bin

# The overlay. Everything outside /out is the workload's, untouched.
FROM scratch
COPY --from=build /home/agent/.local/bin /home/agent/.local/bin

# syntax=docker/dockerfile:1
# Build stage downloads the Aspire CLI with Aspire's official install script:
# https://aspire.dev/get-started/install-cli/
#
# The script selects the release archive for the build architecture and
# verifies its SHA-512 before extracting, so the Kit does not hand-roll the
# download URL or the checksum. Pin `ASPIRE_VERSION`; the AppHost is pinned to
# the same Aspire release family in Zaiware.Platform.AppHost.csproj.
FROM debian:trixie-slim AS build

ARG ASPIRE_VERSION=17.0.0

# Build-only. Docker builds run as root with HOME=/root; the installers choose
# their default paths from HOME. Do not set this at runtime.
ENV HOME=/home/agent

# libicu76 is for the sanity check: the NativeAOT CLI needs ICU to load its
# resources. The sandbox gets ICU from the dotnet Kit, so do not copy those
# libraries into the overlay.
RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates curl tar gzip libicu76 \
 && rm -rf /var/lib/apt/lists/*

# Install at the official default, $HOME/.aspire/bin. The script writes
# .aspire-install.json next to the binary; for a script install the CLI keeps
# logs, cache, hives, and versions in the parent (~/.aspire), which is writable
# by the agent.
#
# --skip-path is required: the script otherwise edits a shell profile we do not
# ship. --version pins the release.
RUN curl -fsSL https://aspire.dev/install.sh -o /tmp/aspire-install.sh \
 && bash /tmp/aspire-install.sh \
      --version "$ASPIRE_VERSION" \
      --skip-path \
 && /home/agent/.aspire/bin/aspire --version \
 && rm -rf /home/agent/.aspire/logs /home/agent/.aspire/cache \
 && chown -R 1000:1000 /home/agent/.aspire

# Copy the install tree (bin only) and expose it through the standard PATH
# directory. Do not copy state directories into the image.
RUN mkdir -p /out/usr/local/bin /out/home/agent/.aspire \
 && cp -a /home/agent/.aspire/bin /out/home/agent/.aspire/ \
 && ln -s /home/agent/.aspire/bin/aspire /out/usr/local/bin/aspire \
 && chown 1000:1000 /out/home/agent /out/home/agent/.aspire \
 && chown -R 1000:1000 /out/home/agent/.aspire/bin

# The overlay. Everything outside /out is the workload's, untouched.
FROM scratch
COPY --from=build /out /

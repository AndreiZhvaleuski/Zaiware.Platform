# syntax=docker/dockerfile:1
# BuildKit resolves FROM to the *target* platform, which is what this needs:
# dotnetup installs for the architecture of its own process and has no
# --architecture option. A plain FROM therefore keeps the SDK's architecture
# equal to the sandbox's, at the cost of emulation when cross-building.
FROM debian:trixie-slim AS build

ARG DOTNET_SDK_VERSION=10.0.401
# dotnetup is in preview; "preview" is the published quality. Override the arg
# to track "daily" while testing one.
ARG DOTNETUP_QUALITY=preview

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates curl libicu76 \
 && rm -rf /var/lib/apt/lists/*

# The script verifies the binary's SHA-512 before installing. Install to the
# absolute path the sandbox will see, so `dotnetup self update` keeps working.
RUN curl -fsSL https://aka.ms/dotnetup/get-dotnetup.sh -o /tmp/get-dotnetup.sh \
 && bash /tmp/get-dotnetup.sh \
      --install-dir /usr/local/lib/dotnetup \
      --quality "$DOTNETUP_QUALITY"

# The workload is a hardened Debian image without libicu, and the .NET host
# aborts at startup when it is missing. Copy just the ICU shared libraries the
# SDK needs, at their multiarch path, so the sandbox's own loader finds them.
# dhi.io/sbx-templates already carries libssl and zlib; ICU is the gap.
RUN for lib in /usr/lib/*-linux-gnu/libicu*.so.*; do \
      target="/out$(dirname "$lib")"; \
      mkdir -p "$target"; \
      cp -a "$lib" "$target/"; \
    done

# Install where the sandbox user will actually run from. HOME decides both the
# data directory (~/.local/share/dotnetup) and the absolute paths recorded in
# dotnetup_manifest.json, so the manifest stays valid after the overlay lands
# on the workload's /home/agent. A tracked install keeps `dotnetup list`,
# `update`, and `uninstall` working in the sandbox.
#
# dotnetup resolves the channel through a releases index before downloading;
# that request occasionally times out ("A task was canceled"), so retry from a
# clean data directory instead of leaving a half-written manifest behind.
RUN mkdir -p /home/agent \
 && set -eux; \
    for attempt in 1 2 3; do \
      if HOME=/home/agent /usr/local/lib/dotnetup/dotnetup install "$DOTNET_SDK_VERSION" --no-progress; then \
        break; \
      fi; \
      if [ "$attempt" = 3 ]; then exit 1; fi; \
      rm -rf /home/agent/.local/share/dotnetup; \
      sleep 5; \
    done; \
    test -x /home/agent/.local/share/dotnetup/dotnet/dotnet

# Stage only the content the workload lacks: the dotnetup binary, the managed
# SDK, and two /usr/local/bin links. /usr/local/bin is on the sandbox PATH, so
# no shell profile or startup hook has to wire the environment.
RUN mkdir -p /out/usr/local/bin /out/usr/local/lib /out/home/agent/.local/share \
 && cp -a /usr/local/lib/dotnetup /out/usr/local/lib/ \
 && cp -a /home/agent/.local/share/dotnetup /out/home/agent/.local/share/ \
 && ln -s /usr/local/lib/dotnetup/dotnetup /out/usr/local/bin/dotnetup \
 && ln -s /home/agent/.local/share/dotnetup/dotnet/dotnet /out/usr/local/bin/dotnet \
 && chown -R 1000:1000 /out/home/agent

# The overlay. Everything outside /out is the workload's, untouched.
FROM scratch
COPY --from=build /out /

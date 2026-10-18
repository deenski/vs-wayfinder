# Build context is an extracted Vintage Story dedicated server, supplied by
# server-image.yml at image build time. Game files are never committed.
FROM mcr.microsoft.com/dotnet/runtime:10.0

LABEL org.opencontainers.image.source="https://github.com/deenski/vs-wayfinder" \
      org.opencontainers.image.description="Private Vintage Story headless server for vs-wayfinder CI. Not for redistribution."

COPY . /vs/
WORKDIR /vs

RUN chmod +x /vs/VintagestoryServer

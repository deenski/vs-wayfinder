# Build context is an extracted Vintage Story dedicated server, supplied by
# server-image.yml at image build time. Game files are never committed.
# Do not set org.opencontainers.image.source. That label links the package to
# this public repository before the first publish. Linking is what makes a
# package inherit the repository's access permissions.
FROM mcr.microsoft.com/dotnet/runtime:10.0

LABEL org.opencontainers.image.description="Private Vintage Story headless server for vs-wayfinder CI. Not for redistribution."

COPY . /vs/
WORKDIR /vs

RUN chmod +x /vs/VintagestoryServer

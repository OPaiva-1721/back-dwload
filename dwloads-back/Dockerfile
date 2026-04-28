FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/VideoDownloader.Api/VideoDownloader.Api.csproj \
    -c Release -o /app/publish --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends \
    ffmpeg \
    curl \
    nodejs \
    unzip \
    && ln -s /usr/bin/nodejs /usr/local/bin/node \
    && rm -rf /var/lib/apt/lists/*

RUN curl -fsSL https://deno.land/install.sh | DENO_INSTALL=/usr/local sh

RUN curl -L https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_linux \
    -o /usr/local/bin/yt-dlp && chmod +x /usr/local/bin/yt-dlp

COPY --from=build /app/publish .
COPY docker-entrypoint.sh /app/docker-entrypoint.sh

RUN mkdir -p /app/downloads \
    && chmod +x /app/docker-entrypoint.sh

EXPOSE 5089
ENV ASPNETCORE_URLS=http://+:5089

ENTRYPOINT ["/app/docker-entrypoint.sh"]

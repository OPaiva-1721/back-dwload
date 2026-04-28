#!/bin/sh
set -e
if [ -n "$YTDLP_COOKIES" ]; then
    printf '%s' "$YTDLP_COOKIES" > /app/cookies.txt
fi
exec dotnet VideoDownloader.Api.dll

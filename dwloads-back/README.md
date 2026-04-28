# Video Downloader API

REST API built with ASP.NET Core (.NET 10) that accepts video URLs from major platforms and returns downloadable MP4 or MP3 files. Downloads run as background jobs tracked in real time via SignalR.

---

## Table of Contents

- [Features](#features)
- [Architecture](#architecture)
- [Project Structure](#project-structure)
- [Prerequisites](#prerequisites)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
- [API Reference](#api-reference)
- [Real-Time Progress (SignalR)](#real-time-progress-signalr)
- [Running the Tests](#running-the-tests)
- [Design Decisions](#design-decisions)

---

## Features

- Download videos as **MP4** or extract audio as **MP3**
- Supported platforms: **YouTube**, **Instagram**, **TikTok**, **Twitter / X**
- Asynchronous downloads via **Hangfire** background jobs
- Real-time progress updates via **SignalR**
- Video metadata lookup (title, thumbnail, duration, available formats)
- Rate limiting (10 requests / minute per IP)
- Structured logging via **Serilog**
- Automatic file cleanup 1 hour after download completes
- Swagger UI for interactive exploration

---

## Architecture

Clean Architecture with a lightweight CQRS approach (MediatR). Layers have a strict one-way dependency rule: outer layers depend on inner ones, never the reverse.

```
┌─────────────────────────────────────────────────┐
│                  Client (HTTP/WS)               │
└───────────────────┬─────────────────────────────┘
                    │ REST + SignalR
┌───────────────────▼─────────────────────────────┐
│              API Layer (Presentation)            │
│         Minimal APIs · Middleware · SignalR      │
└───────────────────┬─────────────────────────────┘
                    │ MediatR
┌───────────────────▼─────────────────────────────┐
│             Application Layer                    │
│      Commands · Queries · DTOs · Interfaces      │
└──────────┬────────────────────┬─────────────────┘
           │                    │
┌──────────▼──────┐   ┌─────────▼───────────────┐
│  Domain Layer   │   │   Infrastructure Layer   │
│ Entities · VOs  │   │ yt-dlp · Storage · Queue │
│ Domain Services │   │ Hangfire · SignalR        │
└─────────────────┘   └─────────────────────────┘
```

Key patterns used:

| Pattern | Where |
|---|---|
| Result\<T\> | All use cases return `Result<T>` — no exception-driven control flow |
| CQRS (lightweight) | Commands and Queries via MediatR pipeline |
| Value Object | `VideoUrl` validates and detects platform at construction |
| Repository | `IDownloadJobRepository` abstracts job state storage |
| Pipeline Behaviors | Logging and FluentValidation wired into MediatR pipeline |

---

## Project Structure

```
VideoDownloaderApi/
├── src/
│   ├── VideoDownloader.Api/                # Presentation layer
│   │   ├── Contracts/                      # Request models
│   │   ├── Endpoints/                      # Minimal API route groups
│   │   └── Middleware/                     # Global exception handler
│   │
│   ├── VideoDownloader.Application/        # Use cases
│   │   ├── Common/
│   │   │   ├── Behaviors/                  # Logging + Validation pipeline behaviors
│   │   │   ├── DTOs/                       # Response records
│   │   │   └── Interfaces/                 # Abstractions (IStorageService, IDownloadQueue, …)
│   │   └── Downloads/
│   │       ├── Commands/RequestDownload/   # Queue a new download
│   │       └── Queries/
│   │           ├── GetDownloadStatus/      # Poll job status
│   │           └── GetVideoMetadata/       # Fetch video metadata
│   │
│   ├── VideoDownloader.Domain/             # Enterprise business rules
│   │   ├── Entities/DownloadJob.cs         # Aggregate root
│   │   ├── ValueObjects/VideoUrl.cs        # URL validation + platform detection
│   │   ├── Enums/                          # DownloadFormat, DownloadStatus, Platform
│   │   ├── Errors/                         # Error, Result<T>, DomainErrors
│   │   └── Interfaces/IDownloadJobRepository.cs
│   │
│   └── VideoDownloader.Infrastructure/     # External concerns
│       ├── Queue/                          # Hangfire queue + DownloadJobProcessor
│       ├── RealTime/                       # SignalR hub + progress notifier
│       ├── Repositories/                   # In-memory job repository
│       ├── Storage/                        # Local file storage
│       └── YtDlp/                          # yt-dlp process wrapper
│
└── tests/
    ├── VideoDownloader.Domain.Tests/       # VideoUrl, DownloadJob, Result<T>
    ├── VideoDownloader.Application.Tests/  # Handler unit tests (NSubstitute mocks)
    └── VideoDownloader.Infrastructure.Tests/ # Repository + DownloadJobProcessor
```

---

## Prerequisites

| Dependency | Version |
|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | 10.0+ |
| [yt-dlp](https://github.com/yt-dlp/yt-dlp) | Latest |
| [ffmpeg](https://ffmpeg.org/download.html) | Any recent (required by yt-dlp for MP3 extraction and muxing) |

**Install yt-dlp:**

```bash
# Windows (via pip)
pip install yt-dlp

# macOS
brew install yt-dlp

# Linux
sudo curl -L https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp -o /usr/local/bin/yt-dlp
sudo chmod a+rx /usr/local/bin/yt-dlp
```

---

## Getting Started

```bash
# Clone the repository
git clone <repo-url>
cd VideoDownloaderApi

# Restore dependencies
dotnet restore

# Run the API
dotnet run --project src/VideoDownloader.Api
```

The API will be available at `http://localhost:5000`. Swagger UI is served at `http://localhost:5000/swagger` in development mode.

The Hangfire dashboard is available at `http://localhost:5000/hangfire`.

---

## Configuration

Settings are read from `appsettings.json` (or environment variables using the standard .NET `__` separator).

```json
{
  "YtDlp": {
    "ExecutablePath": "yt-dlp"
  },
  "Storage": {
    "BasePath": "/tmp/videodownloader",
    "BaseUrl": "http://localhost:5000",
    "FileRetention": "01:00:00"
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    }
  }
}
```

| Key | Default | Description |
|---|---|---|
| `YtDlp:ExecutablePath` | `yt-dlp` | Path to the yt-dlp binary (use full path if not on `$PATH`) |
| `Storage:BasePath` | System temp dir | Directory where downloaded files are stored |
| `Storage:BaseUrl` | `http://localhost:5000` | Base URL used to construct download links returned to clients |
| `Storage:FileRetention` | `01:00:00` | How long files are kept before automatic deletion |

---

## API Reference

### `GET /api/metadata`

Fetch video metadata without downloading.

**Query parameters**

| Parameter | Type | Required | Description |
|---|---|---|---|
| `url` | string | Yes | Full video URL |

**Response `200 OK`**

```json
{
  "title": "Never Gonna Give You Up",
  "thumbnailUrl": "https://i.ytimg.com/vi/dQw4w9WgXcQ/maxresdefault.jpg",
  "duration": "00:03:33",
  "availableFormats": [
    {
      "id": "137",
      "extension": "mp4",
      "quality": "1080",
      "fileSizeBytes": 98765432
    }
  ]
}
```

---

### `POST /api/downloads`

Queue a download job. Returns immediately with a job ID — the download runs in the background.

**Request body**

```json
{
  "url": "https://youtube.com/watch?v=dQw4w9WgXcQ",
  "format": "mp4"
}
```

| Field | Type | Values |
|---|---|---|
| `url` | string | Any supported platform URL |
| `format` | string | `mp4` or `mp3` |

**Response `202 Accepted`**

```json
{
  "jobId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "status": "Queued"
}
```

---

### `GET /api/downloads/{jobId}/status`

Poll a job's status.

**Response `200 OK`**

```json
{
  "jobId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "status": "Completed",
  "progressPercent": 100,
  "downloadUrl": "http://localhost:5000/files/3fa85f64-5717-4562-b3fc-2c963f66afa6.mp4",
  "errorMessage": null
}
```

| `status` | Meaning |
|---|---|
| `Queued` | Job is waiting for a Hangfire worker |
| `Processing` | yt-dlp is running |
| `Completed` | File is ready — `downloadUrl` is populated |
| `Failed` | Something went wrong — `errorMessage` is populated |

---

### `GET /files/{fileName}`

Download a processed file directly.

---

## Real-Time Progress (SignalR)

Connect to the hub at `/hubs/download` to receive live progress instead of polling.

```javascript
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/download")
    .build();

connection.on("ProgressUpdated", (jobId, percent) => {
    console.log(`Job ${jobId}: ${percent}%`);
});

connection.on("DownloadCompleted", (jobId, downloadUrl) => {
    console.log(`Done! Download: ${downloadUrl}`);
});

connection.on("DownloadFailed", (jobId, errorMessage) => {
    console.error(`Failed: ${errorMessage}`);
});

await connection.start();
```

| Event | Payload | Fired when |
|---|---|---|
| `ProgressUpdated` | `(jobId, percent)` | yt-dlp reports a new percentage |
| `DownloadCompleted` | `(jobId, downloadUrl)` | File has been saved and is ready |
| `DownloadFailed` | `(jobId, errorMessage)` | yt-dlp or storage operation failed |

---

## Running the Tests

```bash
# Run all tests
dotnet test VideoDownloader.slnx

# Run a specific project
dotnet test tests/VideoDownloader.Domain.Tests/
dotnet test tests/VideoDownloader.Application.Tests/
dotnet test tests/VideoDownloader.Infrastructure.Tests/
```

**Test coverage summary**

| Project | Tests | What is covered |
|---|---|---|
| `Domain.Tests` | 35 | `VideoUrl` validation and platform detection, `DownloadJob` state machine, `Result<T>` |
| `Application.Tests` | 24 | `RequestDownloadHandler`, `GetDownloadStatusHandler`, `GetVideoMetadataHandler` |
| `Infrastructure.Tests` | 7 | `InMemoryDownloadJobRepository`, `DownloadJobProcessor` success/failure flows |

Tests use **xUnit**, **FluentAssertions**, and **NSubstitute** for mocking.

---

## Design Decisions

**Why Hangfire instead of `IHostedService` + `Channel`?**
Hangfire gives a persistent job queue, retry logic, and a dashboard for free. A hosted service would need all of that reimplemented manually.

**Why in-memory repository?**
The current scope is a single-instance API. Swapping to EF Core or Redis requires only a new `IDownloadJobRepository` implementation — the rest of the codebase is unaffected.

**Why `Result<T>` instead of exceptions?**
Errors like "unsupported platform" or "job not found" are expected outcomes, not exceptional conditions. `Result<T>` makes the caller handle them explicitly and keeps the happy path readable without try/catch.

**Why Minimal APIs?**
Less ceremony than controllers for a focused API surface. Route groups (`MapDownloadEndpoints`, `MapMetadataEndpoints`) keep the code organized without the overhead of the MVC pipeline.

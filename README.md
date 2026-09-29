# DWLoad — back-end

API que recebe a URL de um vídeo (YouTube, Instagram, TikTok, Twitter/X) e devolve um **MP4** ou **MP3**. O download roda em segundo plano (yt-dlp + ffmpeg), o progresso chega em tempo real por SSE e o arquivo fica disponível por tempo limitado.

ASP.NET Core (.NET 10) · Clean Architecture + MediatR · Hangfire · PostgreSQL · SSE · Serilog

## Como funciona

```
POST /api/downloads ──► Hangfire (fila) ──► yt-dlp + ffmpeg ──► /app/downloads
        │                                         │
        └── jobId ◄── SSE: progresso ─────────┘──► GET /files/{arquivo}
```

1. O front consulta `GET /api/metadata?url=` para listar título, miniatura e qualidades.
2. `POST /api/downloads` enfileira o job e responde `202` com o `jobId`.
3. O progresso vem por SSE em `GET /downloads/{jobId}/stream` (ou por polling em `/api/downloads/{jobId}/status`).
4. Ao concluir, o evento `done` traz a `downloadUrl`. Os arquivos expiram (`Storage:FileRetention`, padrão 1 h) e são limpos na inicialização.

## Estrutura

```
dwloads-back/   API .NET (src/ + tests/), Dockerfile — detalhes em dwloads-back/README.md
tools/          yt_cookies: gera o cookies.txt do yt-dlp a partir de um perfil Chrome logado
docs/           planos e notas de projeto
TASKS.md        checklist até o deploy
docker-compose.yml       desenvolvimento local (API)
docker-compose.prod.yml  produção (API + PostgreSQL)
```

## Requisitos

- Docker (caminho mais simples: a imagem já traz yt-dlp, ffmpeg, deno e node), **ou**
- .NET SDK 10, [yt-dlp](https://github.com/yt-dlp/yt-dlp), ffmpeg e deno/node no PATH, e um PostgreSQL acessível.

## Rodando

### Docker (dev)

```bash
touch dwloads-back/cookies.txt      # o compose monta este arquivo; pode ficar vazio
docker compose up --build
```

API em `http://localhost:5089`, Swagger em `/swagger` (ambiente Development) e health check em `/health`.

### Sem Docker

```bash
cd dwloads-back
dotnet run --project src/VideoDownloader.Api
```

Ajuste `ConnectionStrings:DefaultConnection` (Postgres) por variável de ambiente ou `appsettings`. Em Development o CORS já libera `http://localhost:5173`.

### Testes

```bash
cd dwloads-back && dotnet test VideoDownloader.slnx
python -m pytest tools/tests        # ferramenta de cookies (precisa de pytest)
```

## Cookies do YouTube

O YouTube costuma exigir login (“Sign in to confirm you're not a bot”). O backend lê um `cookies.txt` (`YtDlp:CookiesFile`). Para gerá-lo com uma **conta Google secundária**:

```bash
cd tools
pip install playwright
python -m yt_cookies login     # uma vez: entre no YouTube e feche o Chrome
python -m yt_cookies export    # grava dwloads-back/cookies.txt
```

Quando a API responder `YtDlp.AuthRequired`, reexporte. Em deploy sem volume, passe o conteúdo do arquivo na variável `YTDLP_COOKIES` (o entrypoint o grava em `/app/cookies.txt`). Nunca faça commit do `cookies.txt`: ele dá acesso à conta. Detalhes em [`tools/README.md`](tools/README.md).

## Configuração

Variáveis de ambiente usam `__` como separador (`Storage__BaseUrl`).

| Chave | Padrão | Descrição |
|---|---|---|
| `YtDlp:ExecutablePath` | `yt-dlp` | Binário do yt-dlp |
| `YtDlp:CookiesFile` | `/app/cookies.txt` | Cookies em formato Netscape |
| `Storage:BasePath` | `downloads` | Pasta dos arquivos gerados |
| `Storage:BaseUrl` | `http://localhost:5089` | Base das `downloadUrl` retornadas |
| `Storage:FileRetention` | `01:00:00` | Tempo até apagar os arquivos |
| `Cors:AllowedOrigins:0` | — | Origem permitida do front |
| `ConnectionStrings:DefaultConnection` | — | PostgreSQL (jobs e Hangfire) |

## Deploy (produção)

```bash
cp .env.prod.example .env.prod      # preencha POSTGRES_PASSWORD, STORAGE_BASE_URL, CORS_ALLOWED_ORIGIN
docker compose -f docker-compose.prod.yml --env-file .env.prod up -d --build
```

Sobe a API na porta 5089 e um PostgreSQL 16 com volume persistente. O health check usa `/health`; `/health/ready` verifica Postgres e Hangfire. Coloque um proxy com HTTPS na frente e aponte `STORAGE_BASE_URL` para a URL pública.

## Endpoints

| Método | Rota | Descrição |
|---|---|---|
| GET | `/api/metadata?url=` | Título, miniatura, duração e formatos |
| POST | `/api/downloads` | Enfileira `{ url, format: mp4\|mp3, quality }` → `202 { jobId }` |
| GET | `/api/downloads/{jobId}/status` | Status e progresso do job |
| GET | `/files/{fileName}` | Baixa o arquivo pronto |
| GET (SSE) | `/downloads/{jobId}/stream` | Eventos `progress`, `done`, `failed` |
| GET | `/health`, `/health/ready` | Saúde do serviço |

Qualidades aceitas: vídeo `2160p/1080p/720p/480p`; áudio `320kbps/256kbps/192kbps/128kbps`. Há rate limiting por IP e erros no formato `ProblemDetails`. Arquitetura e camadas em [`dwloads-back/README.md`](dwloads-back/README.md) (parte dele ainda cita SignalR, repositório em memória e a porta 5000, anteriores à migração para SSE/PostgreSQL) e em [`dwloads-back/ARCHITECTURE.md`](dwloads-back/ARCHITECTURE.md).

## Aviso legal

Baixe apenas conteúdo seu ou cuja licença permita. Os termos das plataformas proíbem o restante.

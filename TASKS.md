# DWLoad — Plano de Ação até o Deploy

## Fase 1 — Integração básica (bloqueadores)

- [x] **1. Configurar CORS no backend**
  Adicionar `AddCors()` e `UseCors()` no `Program.cs` com política permitindo `http://localhost:5173` (dev) e a origem de produção. `UseCors` deve vir antes do `UseRateLimiter`.

- [x] **2. Alinhar portas entre front e back**
  Atualizar `VITE_API_URL` no `.env.development` para `http://localhost:5089` **ou** adicionar proxy no `vite.config.ts` apontando `/api` para `5089`. Decidir a estratégia e aplicar consistentemente.

- [x] **3. Decidir SSE vs SignalR e unificar**
  Escolher entre: (a) criar endpoint SSE `/downloads/{jobId}/stream` no backend, ou (b) migrar frontend para `@microsoft/signalr` client.
  > Recomendação: manter SignalR (já implementado no back) e substituir `EventSource` no `useJobStream.ts`.

- [x] **4. Implementar cliente de streaming real-time**
  Aplicar a decisão anterior: instalar `@microsoft/signalr` no front, refatorar `useJobStream.ts` para usar `HubConnection`, mapear eventos `ProgressUpdated` / `DownloadCompleted` / `DownloadFailed` para o formato que a UI consome.

- [x] **5. Mapear formatos video/audio → mp4/mp3**
  Em `downloadsApi.ts`, converter `format: 'video'` → `'mp4'` e `format: 'audio'` → `'mp3'` antes do POST `/api/downloads`.

## Fase 2 — Alinhamento de contratos

- [x] **6. Resolver parâmetro quality**
  Decidir: (a) adicionar campo `Quality` no DTO do backend e passar para o downloader, ou (b) remover quality do payload do frontend.

- [x] **7. Integrar endpoint /api/metadata no frontend**
  Criar chamada para `GET /api/metadata?url=` e usar no `QualityPicker` para preencher qualidades reais em vez das hardcoded. Adicionar loading state.

- [x] **8. Corrigir downloadUrl no evento done**
  No backend, montar URL completa com `Storage.BaseUrl + /files/{fileName}` no payload do `DownloadCompleted`. Atualizar `Storage.BaseUrl` no `appsettings.json` para casar com a porta real.

- [x] **9. Desligar mock SSE**
  Setar `VITE_USE_MOCK_SSE=false` no `.env.development` após streaming real funcionar.

## Fase 3 — Qualidade e testes

- [x] **10. Teste end-to-end do fluxo completo**
  Subir back + front localmente e validar: colar URL → fetch metadata → escolher qualidade → POST download → receber progress via stream → done com downloadUrl → baixar arquivo. Testar caminho feliz + URL inválida + erro de download.

- [x] **11. Tratamento de erros padronizado**
  Garantir que backend retorna `ProblemDetails` consistente em todos os endpoints e que frontend exibe mensagens úteis. Mapear: `400` (URL inválida), `429` (rate limit), `500` (falha yt-dlp).

## Fase 4 — Deploy

- [x] **12. Configurar variáveis de ambiente para produção**
  Criar `.env.production` no frontend com `VITE_API_URL` apontando para domínio de prod. No backend, mover origens de CORS e `Storage.BaseUrl` para `appsettings.Production.json` ou variáveis de ambiente.

- [ ] **13. Build de produção do frontend**
  Rodar `pnpm build`, validar output em `dist/`, testar com `pnpm preview` apontando para backend real.

- [ ] **14. Build e publish do backend**
  Rodar `dotnet publish -c Release`. Validar que `yt-dlp` e `ffmpeg` estão disponíveis no ambiente alvo.

- [x] **15. Dockerizar backend** _(opcional mas recomendado)_
  Criar `Dockerfile` baseado em `aspnet:8.0` com `yt-dlp` e `ffmpeg` pré-instalados. Configurar volume para a pasta de downloads temporários.

- [ ] **16. Escolher hospedagem e fazer deploy**
  Frontend → Vercel / Netlify / Cloudflare Pages
  Backend → Azure App Service / Railway / Fly.io / VPS

- [ ] **17. Configurar domínio e HTTPS**
  Apontar domínios para front e back. HTTPS obrigatório para SignalR/SSE em browsers modernos. Atualizar CORS com domínios finais e remover `localhost`.

- [ ] **18. Smoke test em produção**
  Repetir fluxo end-to-end no ambiente de produção. Verificar CORS, streaming via HTTPS, download de arquivo, rate limit, e limpeza de arquivos temporários (Hangfire).

- [ ] **19. Observabilidade básica**
  Configurar nível de log adequado em prod. Adicionar health check endpoint (`/health`). Opcional: Sentry / Application Insights para tracking de erros.

# DWLoad Production Readiness Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tornar o DWLoad pronto para produção resolvendo persistência, confiabilidade, observabilidade e segurança de configuração.

**Architecture:** As fases são independentes e podem ser executadas e deployadas separadamente. Fase 1 desbloqueia produção. Fases 2-4 melhoram confiabilidade e operabilidade. Cada tarefa produz software testável por conta própria.

**Tech Stack:** .NET 10, ASP.NET Core Minimal APIs, Hangfire, EF Core 10, Npgsql, xUnit, FluentAssertions, NSubstitute, React 19, Vite, TypeScript.

---

## Mapa de Arquivos

| Arquivo | Ação | Responsabilidade |
|---------|------|-----------------|
| `dwloads-back/src/VideoDownloader.Infrastructure/VideoDownloader.Infrastructure.csproj` | Modify | Adicionar NuGets de EF Core e Hangfire.PostgreSql |
| `dwloads-back/src/VideoDownloader.Infrastructure/Persistence/AppDbContext.cs` | Create | DbContext com configuração de DownloadJob |
| `dwloads-back/src/VideoDownloader.Infrastructure/Persistence/DownloadJobConfiguration.cs` | Create | Mapeamento EF Core para DownloadJob |
| `dwloads-back/src/VideoDownloader.Infrastructure/Repositories/EfDownloadJobRepository.cs` | Create | Implementação persistente de IDownloadJobRepository |
| `dwloads-back/src/VideoDownloader.Infrastructure/DependencyInjection.cs` | Modify | Trocar in-memory por EF Core + Hangfire.PostgreSql |
| `dwloads-back/src/VideoDownloader.Infrastructure/Queue/YtDlpConcurrencyLimiter.cs` | Create | SemaphoreSlim para limitar processos yt-dlp simultâneos |
| `dwloads-back/src/VideoDownloader.Infrastructure/YtDlp/YtDlpVideoService.cs` | Modify | Injetar limiter + expor stderr nos erros |
| `dwloads-back/src/VideoDownloader.Infrastructure/Jobs/StartupCleanupJob.cs` | Create | IHostedService que limpa arquivos expirados no startup |
| `dwloads-back/src/VideoDownloader.Api/Program.cs` | Modify | Mapear /health e registrar StartupCleanupJob |
| `dwloads-back/src/VideoDownloader.Api/appsettings.Production.json` | Modify | Adicionar placeholder de ConnectionStrings |
| `docker-compose.prod.yml` | Modify | Adicionar serviço postgres + env DATABASE_URL |
| `dwloads-front/.env.development` | Modify | VITE_USE_MOCK_SSE=true |
| `dwloads-back/tests/VideoDownloader.Infrastructure.Tests/VideoDownloader.Infrastructure.Tests.csproj` | Modify | Adicionar Npgsql.EntityFrameworkCore.PostgreSQL para testes |
| `dwloads-back/tests/VideoDownloader.Infrastructure.Tests/Repositories/EfDownloadJobRepositoryTests.cs` | Create | Testes de integração do repositório EF Core |

---

## Fase 1 — Bloqueadores de Produção

---

### Task 1: PostgreSQL no docker-compose.prod.yml

**Files:**
- Modify: `docker-compose.prod.yml`
- Modify: `dwloads-back/src/VideoDownloader.Api/appsettings.Production.json`

- [ ] **Step 1: Atualizar docker-compose.prod.yml para incluir PostgreSQL**

```yaml
# docker-compose.prod.yml
services:
  db:
    image: postgres:16-alpine
    restart: unless-stopped
    environment:
      POSTGRES_USER: dwload
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
      POSTGRES_DB: dwload
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U dwload"]
      interval: 10s
      timeout: 5s
      retries: 5

  api:
    build:
      context: ./dwloads-back
    ports:
      - "5089:5089"
    depends_on:
      db:
        condition: service_healthy
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=http://+:5089
      - Storage__BaseUrl=${STORAGE_BASE_URL}
      - Cors__AllowedOrigins__0=${CORS_ALLOWED_ORIGIN}
      - ConnectionStrings__DefaultConnection=Host=db;Port=5432;Database=dwload;Username=dwload;Password=${POSTGRES_PASSWORD}
    volumes:
      - downloads:/app/downloads
    restart: unless-stopped

volumes:
  downloads:
  pgdata:
```

- [ ] **Step 2: Adicionar placeholder de ConnectionStrings ao appsettings.Production.json**

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Warning",
      "Override": {
        "Microsoft": "Warning",
        "Hangfire": "Warning"
      }
    }
  },
  "Cors": {
    "AllowedOrigins": []
  },
  "Storage": {
    "BasePath": "/app/downloads",
    "BaseUrl": "",
    "FileRetention": "01:00:00"
  },
  "ConnectionStrings": {
    "DefaultConnection": ""
  }
}
```

- [ ] **Step 3: Criar arquivo .env.prod.example na raiz com variáveis necessárias**

```bash
# .env.prod.example — copie para .env.prod e preencha os valores reais
POSTGRES_PASSWORD=senha_forte_aqui
STORAGE_BASE_URL=http://SEU_IP:5089
CORS_ALLOWED_ORIGIN=https://SEU_PROJETO.vercel.app
```

- [ ] **Step 4: Commit**

```bash
git add docker-compose.prod.yml dwloads-back/src/VideoDownloader.Api/appsettings.Production.json .env.prod.example
git commit -m "chore: add postgres to prod compose and document required env vars"
```

---

### Task 2: Hangfire → PostgreSQL

**Files:**
- Modify: `dwloads-back/src/VideoDownloader.Infrastructure/VideoDownloader.Infrastructure.csproj`
- Modify: `dwloads-back/src/VideoDownloader.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Adicionar NuGet Hangfire.PostgreSql ao projeto Infrastructure**

```xml
<!-- dwloads-back/src/VideoDownloader.Infrastructure/VideoDownloader.Infrastructure.csproj -->
<!-- Substituir <PackageReference Include="Hangfire.InMemory" Version="1.0.0" /> por: -->
<PackageReference Include="Hangfire.PostgreSql" Version="1.20.9" />
```

O arquivo completo deve ficar:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\VideoDownloader.Application\VideoDownloader.Application.csproj" />
    <ProjectReference Include="..\VideoDownloader.Domain\VideoDownloader.Domain.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Hangfire.AspNetCore" Version="1.8.23" />
    <PackageReference Include="Hangfire.Core" Version="1.8.23" />
    <PackageReference Include="Hangfire.PostgreSql" Version="1.20.9" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="10.0.0" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.0">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Serilog.Extensions.Hosting" Version="10.0.0" />
    <PackageReference Include="Serilog.Sinks.Console" Version="6.1.1" />
  </ItemGroup>

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Executar `dotnet restore` e confirmar que não há erros**

```bash
cd dwloads-back
dotnet restore
```

Expected: `Restore succeeded.`

- [ ] **Step 3: Atualizar DependencyInjection.cs para usar PostgreSQL**

O trecho de Hangfire em `DependencyInjection.cs` deve ser alterado de:

```csharp
services.AddHangfire(cfg => cfg.UseInMemoryStorage());
```

Para:

```csharp
services.AddHangfire(cfg =>
    cfg.UsePostgreSqlStorage(o =>
        o.UseNpgsqlConnection(config.GetConnectionString("DefaultConnection"))));
```

O arquivo completo:

```csharp
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VideoDownloader.Application.Common.Interfaces;
using VideoDownloader.Domain.Interfaces;
using VideoDownloader.Infrastructure.Queue;
using VideoDownloader.Infrastructure.RealTime;
using VideoDownloader.Infrastructure.Repositories;
using VideoDownloader.Infrastructure.Storage;
using VideoDownloader.Infrastructure.YtDlp;

namespace VideoDownloader.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration config)
    {
        services.Configure<YtDlpOptions>(config.GetSection("YtDlp"));
        services.Configure<StorageOptions>(config.GetSection("Storage"));

        services.AddSingleton<IDownloadJobRepository, InMemoryDownloadJobRepository>();
        services.AddScoped<IVideoMetadataService, YtDlpVideoService>();
        services.AddScoped<IVideoDownloadService, YtDlpVideoService>();
        services.AddScoped<IStorageService, LocalStorageService>();
        services.AddScoped<IDownloadQueue, HangfireDownloadQueue>();
        services.AddSingleton<SseConnectionManager>();
        services.AddScoped<IProgressNotifier, SseProgressNotifier>();

        services.AddScoped<DownloadJobProcessor>();
        services.AddScoped<FileCleanupJob>();

        services.AddHangfire(cfg =>
            cfg.UsePostgreSqlStorage(o =>
                o.UseNpgsqlConnection(config.GetConnectionString("DefaultConnection"))));
        services.AddHangfireServer(opts => opts.WorkerCount = 2);

        return services;
    }
}
```

- [ ] **Step 4: Build para confirmar que compila**

```bash
cd dwloads-back
dotnet build
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add dwloads-back/src/VideoDownloader.Infrastructure/
git commit -m "feat: migrate Hangfire from in-memory to PostgreSQL storage"
```

---

### Task 3: Repositório persistente com EF Core

**Files:**
- Create: `dwloads-back/src/VideoDownloader.Infrastructure/Persistence/AppDbContext.cs`
- Create: `dwloads-back/src/VideoDownloader.Infrastructure/Persistence/DownloadJobConfiguration.cs`
- Create: `dwloads-back/src/VideoDownloader.Infrastructure/Repositories/EfDownloadJobRepository.cs`
- Modify: `dwloads-back/src/VideoDownloader.Infrastructure/DependencyInjection.cs`
- Modify: `dwloads-back/tests/VideoDownloader.Infrastructure.Tests/VideoDownloader.Infrastructure.Tests.csproj`
- Create: `dwloads-back/tests/VideoDownloader.Infrastructure.Tests/Repositories/EfDownloadJobRepositoryTests.cs`

- [ ] **Step 1: Adicionar NuGet de testes ao projeto de testes**

```xml
<!-- dwloads-back/tests/VideoDownloader.Infrastructure.Tests/VideoDownloader.Infrastructure.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="FluentAssertions" Version="7.2.0" />
    <PackageReference Include="NSubstitute" Version="5.3.0" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.6" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="10.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\VideoDownloader.Domain\VideoDownloader.Domain.csproj" />
    <ProjectReference Include="..\..\src\VideoDownloader.Application\VideoDownloader.Application.csproj" />
    <ProjectReference Include="..\..\src\VideoDownloader.Infrastructure\VideoDownloader.Infrastructure.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Escrever o teste que vai falhar**

```csharp
// dwloads-back/tests/VideoDownloader.Infrastructure.Tests/Repositories/EfDownloadJobRepositoryTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VideoDownloader.Domain.Entities;
using VideoDownloader.Domain.Enums;
using VideoDownloader.Domain.ValueObjects;
using VideoDownloader.Infrastructure.Persistence;
using VideoDownloader.Infrastructure.Repositories;

namespace VideoDownloader.Infrastructure.Tests.Repositories;

public sealed class EfDownloadJobRepositoryTests : IDisposable
{
    private readonly AppDbContext _ctx;
    private readonly EfDownloadJobRepository _repo;

    public EfDownloadJobRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _ctx = new AppDbContext(options);
        _repo = new EfDownloadJobRepository(_ctx);
    }

    [Fact]
    public async Task AddAsync_persists_job_and_GetByIdAsync_returns_it()
    {
        var url = VideoUrl.Create("https://www.youtube.com/watch?v=dQw4w9WgXcQ").Value!;
        var job = DownloadJob.Create(url, DownloadFormat.Mp4, "1080p", "Test Title");

        await _repo.AddAsync(job);

        var retrieved = await _repo.GetByIdAsync(job.Id);

        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(job.Id);
        retrieved.Title.Should().Be("Test Title");
        retrieved.Status.Should().Be(DownloadStatus.Queued);
    }

    [Fact]
    public async Task UpdateAsync_persists_status_change()
    {
        var url = VideoUrl.Create("https://www.youtube.com/watch?v=dQw4w9WgXcQ").Value!;
        var job = DownloadJob.Create(url, DownloadFormat.Mp4, "1080p");
        await _repo.AddAsync(job);

        job.UpdateProgress(50);
        await _repo.UpdateAsync(job);

        var retrieved = await _repo.GetByIdAsync(job.Id);
        retrieved!.ProgressPercent.Should().Be(50);
        retrieved.Status.Should().Be(DownloadStatus.Processing);
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_for_unknown_id()
    {
        var result = await _repo.GetByIdAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    public void Dispose() => _ctx.Dispose();
}
```

- [ ] **Step 3: Rodar o teste para confirmar que falha**

```bash
cd dwloads-back
dotnet test tests/VideoDownloader.Infrastructure.Tests/ --filter "EfDownloadJobRepositoryTests" -v minimal
```

Expected: FAIL com `The type 'EfDownloadJobRepository' or 'AppDbContext' could not be found.`

- [ ] **Step 4: Criar DownloadJobConfiguration**

```csharp
// dwloads-back/src/VideoDownloader.Infrastructure/Persistence/DownloadJobConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VideoDownloader.Domain.Entities;
using VideoDownloader.Domain.Enums;
using VideoDownloader.Domain.ValueObjects;

namespace VideoDownloader.Infrastructure.Persistence;

public sealed class DownloadJobConfiguration : IEntityTypeConfiguration<DownloadJob>
{
    public void Configure(EntityTypeBuilder<DownloadJob> builder)
    {
        builder.ToTable("download_jobs");
        builder.HasKey(j => j.Id);

        builder.Property(j => j.Id)
            .HasColumnName("id");

        var urlConverter = new ValueConverter<VideoUrl, string>(
            v => v.Value,
            s => VideoUrl.Create(s).Value!);

        builder.Property(j => j.Url)
            .HasConversion(urlConverter)
            .HasColumnName("url")
            .HasMaxLength(2048)
            .IsRequired();

        builder.Property(j => j.Format)
            .HasConversion<string>()
            .HasColumnName("format")
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(j => j.Quality)
            .HasColumnName("quality")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(j => j.Status)
            .HasConversion<string>()
            .HasColumnName("status")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(j => j.ProgressPercent)
            .HasColumnName("progress_percent");

        builder.Property(j => j.OutputFilePath)
            .HasColumnName("output_file_path")
            .HasMaxLength(1024);

        builder.Property(j => j.ErrorMessage)
            .HasColumnName("error_message")
            .HasMaxLength(2048);

        builder.Property(j => j.Title)
            .HasColumnName("title")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(j => j.ThumbnailUrl)
            .HasColumnName("thumbnail_url")
            .HasMaxLength(2048)
            .IsRequired();

        builder.Property(j => j.Duration)
            .HasColumnName("duration")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(j => j.FileSizeBytes)
            .HasColumnName("file_size_bytes");

        builder.Property(j => j.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(j => j.CompletedAt)
            .HasColumnName("completed_at");
    }
}
```

- [ ] **Step 5: Criar AppDbContext**

```csharp
// dwloads-back/src/VideoDownloader.Infrastructure/Persistence/AppDbContext.cs
using Microsoft.EntityFrameworkCore;
using VideoDownloader.Domain.Entities;

namespace VideoDownloader.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<DownloadJob> DownloadJobs => Set<DownloadJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
```

- [ ] **Step 6: Criar EfDownloadJobRepository**

```csharp
// dwloads-back/src/VideoDownloader.Infrastructure/Repositories/EfDownloadJobRepository.cs
using Microsoft.EntityFrameworkCore;
using VideoDownloader.Domain.Entities;
using VideoDownloader.Domain.Interfaces;
using VideoDownloader.Infrastructure.Persistence;

namespace VideoDownloader.Infrastructure.Repositories;

public sealed class EfDownloadJobRepository(AppDbContext ctx) : IDownloadJobRepository
{
    public async Task<DownloadJob?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await ctx.DownloadJobs.FindAsync([id], ct);

    public async Task AddAsync(DownloadJob job, CancellationToken ct = default)
    {
        await ctx.DownloadJobs.AddAsync(job, ct);
        await ctx.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(DownloadJob job, CancellationToken ct = default)
    {
        ctx.DownloadJobs.Update(job);
        await ctx.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 7: Rodar os testes e confirmar que passam**

```bash
cd dwloads-back
dotnet test tests/VideoDownloader.Infrastructure.Tests/ --filter "EfDownloadJobRepositoryTests" -v minimal
```

Expected: `Passed! - 3 tests`

- [ ] **Step 8: Atualizar DependencyInjection.cs para usar EF Core e EfDownloadJobRepository**

```csharp
// dwloads-back/src/VideoDownloader.Infrastructure/DependencyInjection.cs
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VideoDownloader.Application.Common.Interfaces;
using VideoDownloader.Domain.Interfaces;
using VideoDownloader.Infrastructure.Persistence;
using VideoDownloader.Infrastructure.Queue;
using VideoDownloader.Infrastructure.RealTime;
using VideoDownloader.Infrastructure.Repositories;
using VideoDownloader.Infrastructure.Storage;
using VideoDownloader.Infrastructure.YtDlp;

namespace VideoDownloader.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration config)
    {
        services.Configure<YtDlpOptions>(config.GetSection("YtDlp"));
        services.Configure<StorageOptions>(config.GetSection("Storage"));

        var connectionString = config.GetConnectionString("DefaultConnection")!;

        services.AddDbContext<AppDbContext>(opts =>
            opts.UseNpgsql(connectionString));

        services.AddScoped<IDownloadJobRepository, EfDownloadJobRepository>();
        services.AddScoped<IVideoMetadataService, YtDlpVideoService>();
        services.AddScoped<IVideoDownloadService, YtDlpVideoService>();
        services.AddScoped<IStorageService, LocalStorageService>();
        services.AddScoped<IDownloadQueue, HangfireDownloadQueue>();
        services.AddSingleton<SseConnectionManager>();
        services.AddScoped<IProgressNotifier, SseProgressNotifier>();

        services.AddScoped<DownloadJobProcessor>();
        services.AddScoped<FileCleanupJob>();

        services.AddHangfire(cfg =>
            cfg.UsePostgreSqlStorage(o =>
                o.UseNpgsqlConnection(connectionString)));
        services.AddHangfireServer(opts => opts.WorkerCount = 2);

        return services;
    }
}
```

- [ ] **Step 9: Adicionar migration EF Core**

```bash
cd dwloads-back
dotnet ef migrations add InitialCreate \
  --project src/VideoDownloader.Infrastructure \
  --startup-project src/VideoDownloader.Api \
  --output-dir Persistence/Migrations
```

Expected: `Build succeeded. Done. To undo this action, use 'ef migrations remove'`

- [ ] **Step 10: Aplicar migration automaticamente no startup do Program.cs**

Em `dwloads-back/src/VideoDownloader.Api/Program.cs`, adicionar antes de `app.Run()`:

```csharp
// Aplica migrations pendentes automaticamente no startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}
```

O arquivo completo de `Program.cs` deve ficar:

```csharp
using System.Threading.RateLimiting;
using Hangfire;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;
using VideoDownloader.Api.Endpoints;
using VideoDownloader.Api.Middleware;
using VideoDownloader.Application;
using VideoDownloader.Infrastructure;
using VideoDownloader.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .Enrich.FromLogContext()
       .WriteTo.Console());

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Video Downloader API", Version = "v1" });
});

builder.Services.AddRateLimiter(opts =>
    opts.AddFixedWindowLimiter("ip-limit", o =>
    {
        o.PermitLimit = 10;
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueLimit = 5;
    }));

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];

builder.Services.AddCors(options =>
    options.AddPolicy("AllowFrontend", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.UseMiddleware<ExceptionMiddleware>();
app.UseCors("AllowFrontend");
app.UseRateLimiter();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("EnableSwagger"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapMetadataEndpoints();
app.MapDownloadEndpoints();
app.MapStreamEndpoints();
app.UseHangfireDashboard("/hangfire");

app.Run();
```

- [ ] **Step 11: Build final para confirmar que compila**

```bash
cd dwloads-back
dotnet build
```

Expected: `Build succeeded.`

- [ ] **Step 12: Commit**

```bash
git add dwloads-back/src/VideoDownloader.Infrastructure/ \
        dwloads-back/src/VideoDownloader.Api/Program.cs \
        dwloads-back/tests/VideoDownloader.Infrastructure.Tests/
git commit -m "feat: replace in-memory repository with EF Core + PostgreSQL"
```

---

## Fase 2 — Confiabilidade

---

### Task 4: Limitar concorrência de processos yt-dlp

**Files:**
- Create: `dwloads-back/src/VideoDownloader.Infrastructure/Queue/YtDlpConcurrencyLimiter.cs`
- Modify: `dwloads-back/src/VideoDownloader.Infrastructure/YtDlp/YtDlpVideoService.cs`
- Modify: `dwloads-back/src/VideoDownloader.Infrastructure/DependencyInjection.cs`

**Contexto:** Sem limite, cada Hangfire worker inicia um processo yt-dlp. Com 2 workers e picos, múltiplos processos simultâneos podem saturar CPU/memória. O `SemaphoreSlim` garante no máximo N processos yt-dlp simultâneos independentemente da quantidade de workers.

- [ ] **Step 1: Criar YtDlpConcurrencyLimiter**

```csharp
// dwloads-back/src/VideoDownloader.Infrastructure/Queue/YtDlpConcurrencyLimiter.cs
namespace VideoDownloader.Infrastructure.Queue;

public sealed class YtDlpConcurrencyLimiter(int maxConcurrent = 2) : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(maxConcurrent, maxConcurrent);

    public Task WaitAsync(CancellationToken ct) => _semaphore.WaitAsync(ct);

    public void Release() => _semaphore.Release();

    public void Dispose() => _semaphore.Dispose();
}
```

- [ ] **Step 2: Injetar YtDlpConcurrencyLimiter em YtDlpVideoService**

Alterar a assinatura do construtor de `YtDlpVideoService` e envolver `DownloadAsync` com o semáforo:

```csharp
// dwloads-back/src/VideoDownloader.Infrastructure/YtDlp/YtDlpVideoService.cs
// Alterar o construtor:
public sealed partial class YtDlpVideoService(
    IOptions<YtDlpOptions> options,
    YtDlpConcurrencyLimiter limiter,
    ILogger<YtDlpVideoService> logger)
    : IVideoMetadataService, IVideoDownloadService
{
    private readonly string _ytDlpPath = options.Value.ExecutablePath;
    private readonly string? _cookiesFile = options.Value.CookiesFile;

    // ... restante igual ...

    public async Task<Result<string>> DownloadAsync(
        Guid jobId, VideoUrl url, DownloadFormat format, string quality,
        IProgress<int> progress, CancellationToken ct)
    {
        await limiter.WaitAsync(ct);
        try
        {
            // ... corpo existente de DownloadAsync sem alteração ...
            var outputTemplate = Path.Combine(Path.GetTempPath(), $"{jobId}.%(ext)s");
            string[] args = [
                ..BaseArgs(),
                "--no-playlist",
                "--write-info-json",
                ..(format == DownloadFormat.Mp3
                    ? BuildAudioArgs(quality, outputTemplate, url.Value)
                    : BuildVideoArgs(quality, outputTemplate, url.Value))
            ];

            try
            {
                await RunWithProgressAsync(args, progress, ct);

                var finalPath = Directory
                    .GetFiles(Path.GetTempPath(), $"{jobId}.*")
                    .FirstOrDefault(f => !f.EndsWith(".info.json", StringComparison.OrdinalIgnoreCase));

                return finalPath is null
                    ? Result<string>.Failure(new Error("YtDlp.OutputNotFound", "Download output file not found."))
                    : Result<string>.Success(finalPath);
            }
            catch (OperationCanceledException)
            {
                return Result<string>.Failure(new Error("YtDlp.Cancelled", "Download was cancelled."));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Download failed for job {JobId}", jobId);
                return Result<string>.Failure(new Error("YtDlp.DownloadFailed", ex.Message));
            }
        }
        finally
        {
            limiter.Release();
        }
    }
```

- [ ] **Step 3: Registrar YtDlpConcurrencyLimiter como Singleton em DependencyInjection.cs**

Adicionar em `DependencyInjection.cs`, antes das outras injeções de infra:

```csharp
services.AddSingleton(new YtDlpConcurrencyLimiter(maxConcurrent: 2));
```

- [ ] **Step 4: Build**

```bash
cd dwloads-back && dotnet build
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add dwloads-back/src/VideoDownloader.Infrastructure/
git commit -m "feat: limit concurrent yt-dlp processes via semaphore"
```

---

### Task 5: Expor stderr do yt-dlp nos erros

**Files:**
- Modify: `dwloads-back/src/VideoDownloader.Infrastructure/YtDlp/YtDlpVideoService.cs`

**Contexto:** Atualmente erros chegam ao cliente como "Could not retrieve video metadata." sem nenhum detalhe. O yt-dlp escreve a razão real no stderr (ex: "Video unavailable", "Sign in to confirm your age"). Capturar e incluir isso facilita muito o debugging.

- [ ] **Step 1: Capturar stderr em RunWithProgressAsync e incluir no erro de DownloadAsync**

Alterar `RunWithProgressAsync` para retornar o stderr capturado:

```csharp
// No arquivo YtDlpVideoService.cs

// Alterar a assinatura de RunWithProgressAsync:
private async Task<string> RunWithProgressAsync(string[] args, IProgress<int> progress, CancellationToken ct)
{
    using var process = CreateProcess(args);
    var stderrBuilder = new System.Text.StringBuilder();

    process.OutputDataReceived += (_, e) =>
    {
        if (e.Data is null) return;
        var match = ProgressRegex().Match(e.Data);
        if (match.Success && double.TryParse(
                match.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var pct))
        {
            progress.Report((int)pct);
        }
    };

    process.ErrorDataReceived += (_, e) =>
    {
        if (e.Data is not null)
            stderrBuilder.AppendLine(e.Data);
    };

    process.Start();
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();

    try
    {
        await process.WaitForExitAsync(ct);
    }
    catch (OperationCanceledException)
    {
        try { process.Kill(entireProcessTree: true); } catch { }
        throw;
    }

    if (process.ExitCode != 0)
    {
        var stderr = stderrBuilder.ToString().Trim();
        throw new InvalidOperationException(
            $"yt-dlp exited with code {process.ExitCode}. {(string.IsNullOrEmpty(stderr) ? string.Empty : $"stderr: {stderr}")}");
    }

    return stderrBuilder.ToString();
}
```

Também alterar `GetMetadataAsync` para capturar stderr via `RunAsync` — o `RunAsync` existente já inclui stderr na exception message, mas vamos garantir que o erro retornado ao cliente seja informativo:

```csharp
// Em GetMetadataAsync, alterar o bloco catch para incluir o stderr na mensagem de erro:
catch (Exception ex)
{
    logger.LogError(ex, "Failed to fetch metadata for {Url}", url.Value);
    var detail = ex.Message.Length > 300 ? ex.Message[..300] : ex.Message;
    return Result<VideoMetadataResponse>.Failure(
        new Error("YtDlp.MetadataFailed", $"Could not retrieve video metadata. {detail}"));
}
```

- [ ] **Step 2: Build e rodar os testes de infra existentes**

```bash
cd dwloads-back
dotnet build && dotnet test tests/VideoDownloader.Infrastructure.Tests/ -v minimal
```

Expected: `Build succeeded. Passed!`

- [ ] **Step 3: Commit**

```bash
git add dwloads-back/src/VideoDownloader.Infrastructure/YtDlp/YtDlpVideoService.cs
git commit -m "fix: include yt-dlp stderr in error messages for better diagnostics"
```

---

### Task 6: Cleanup de arquivos expirados no startup

**Files:**
- Create: `dwloads-back/src/VideoDownloader.Infrastructure/Jobs/StartupCleanupJob.cs`
- Modify: `dwloads-back/src/VideoDownloader.Infrastructure/DependencyInjection.cs`

**Contexto:** Se a API crashar, os jobs de cleanup agendados no Hangfire são perdidos (mesmo após migrar para PostgreSQL, jobs que nunca foram enfileirados não existem). O `IHostedService` roda no startup e remove arquivos mais antigos que `FileRetention` antes de aceitar novas requisições.

- [ ] **Step 1: Criar StartupCleanupJob**

```csharp
// dwloads-back/src/VideoDownloader.Infrastructure/Jobs/StartupCleanupJob.cs
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VideoDownloader.Infrastructure.Storage;

namespace VideoDownloader.Infrastructure.Jobs;

public sealed class StartupCleanupJob(
    IOptions<StorageOptions> storageOptions,
    ILogger<StartupCleanupJob> logger) : IHostedService
{
    public Task StartAsync(CancellationToken ct)
    {
        var opts = storageOptions.Value;
        if (!Directory.Exists(opts.BasePath))
        {
            logger.LogInformation("Storage directory {Path} does not exist, skipping startup cleanup", opts.BasePath);
            return Task.CompletedTask;
        }

        var cutoff = DateTimeOffset.UtcNow - opts.FileRetention;
        var deleted = 0;

        foreach (var file in Directory.GetFiles(opts.BasePath))
        {
            try
            {
                var lastWrite = new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero);
                if (lastWrite < cutoff)
                {
                    File.Delete(file);
                    deleted++;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete expired file {File} during startup cleanup", file);
            }
        }

        if (deleted > 0)
            logger.LogInformation("Startup cleanup: deleted {Count} expired file(s) from {Path}", deleted, opts.BasePath);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

- [ ] **Step 2: Registrar em DependencyInjection.cs**

Adicionar em `DependencyInjection.cs`:

```csharp
services.AddHostedService<StartupCleanupJob>();
```

Adicionar o using no topo do arquivo:

```csharp
using VideoDownloader.Infrastructure.Jobs;
```

- [ ] **Step 3: Build**

```bash
cd dwloads-back && dotnet build
```

Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add dwloads-back/src/VideoDownloader.Infrastructure/
git commit -m "feat: clean expired download files on startup to prevent disk accumulation"
```

---

## Fase 3 — Observabilidade

---

### Task 7: Health check endpoint

**Files:**
- Modify: `dwloads-back/src/VideoDownloader.Api/VideoDownloader.Api.csproj`
- Modify: `dwloads-back/src/VideoDownloader.Api/Program.cs`

- [ ] **Step 1: Adicionar NuGet de health checks**

Adicionar em `VideoDownloader.Api.csproj`:

```xml
<PackageReference Include="AspNetCore.HealthChecks.Npgsql" Version="9.0.0" />
```

- [ ] **Step 2: Registrar health checks em Program.cs**

Adicionar antes de `builder.Services.AddRateLimiter(...)`:

```csharp
builder.Services.AddHealthChecks()
    .AddNpgSql(
        builder.Configuration.GetConnectionString("DefaultConnection")!,
        name: "postgres",
        tags: ["db", "ready"])
    .AddHangfire(opts => opts.MinimumAvailableServers = 1,
        name: "hangfire",
        tags: ["queue", "ready"]);
```

Adicionar o endpoint após `app.UseRateLimiter()`:

```csharp
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
```

- [ ] **Step 3: Build**

```bash
cd dwloads-back && dotnet build
```

Expected: `Build succeeded.`

- [ ] **Step 4: Testar manualmente (requer postgres rodando)**

```bash
curl http://localhost:5089/health
```

Expected: `Healthy`

- [ ] **Step 5: Atualizar docker-compose.prod.yml para usar o health check no healthcheck do Docker**

No serviço `api` em `docker-compose.prod.yml`, adicionar:

```yaml
    healthcheck:
      test: ["CMD-SHELL", "curl -f http://localhost:5089/health || exit 1"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 20s
```

- [ ] **Step 6: Commit**

```bash
git add dwloads-back/src/VideoDownloader.Api/ docker-compose.prod.yml
git commit -m "feat: add /health and /health/ready endpoints with postgres + hangfire checks"
```

---

## Fase 4 — Frontend

---

### Task 8: Mock SSE ativo por padrão em desenvolvimento

**Files:**
- Modify: `dwloads-front/.env.development`

**Contexto:** Com `VITE_USE_MOCK_SSE=false`, o frontend trava no estado "baixando" se o backend estiver offline. Ativar o mock por padrão permite desenvolver e testar o fluxo completo de UI sem precisar do backend.

- [ ] **Step 1: Alterar .env.development**

```env
# dwloads-front/.env.development
VITE_API_URL=http://localhost:5089
VITE_USE_MOCK_SSE=true
```

- [ ] **Step 2: Verificar que o mock SSE funciona no browser**

```bash
cd dwloads-front && npm run dev
```

Abrir http://localhost:5173, colar qualquer URL no campo de URL, clicar em Launch. O download deve progredir via mock sem precisar do backend.

- [ ] **Step 3: Commit**

```bash
git add dwloads-front/.env.development
git commit -m "chore: enable mock SSE by default in dev for offline-capable frontend development"
```

---

## Ordem de Execução Recomendada

```
Fase 1 (Task 1 → 2 → 3) → deploy de teste com postgres
       ↓
Fase 2 (Task 4 → 5 → 6) → deploy com confiabilidade melhorada
       ↓
Fase 3 (Task 7)          → deploy com observabilidade
       ↓
Fase 4 (Task 8)          → frontend (pode ser feito a qualquer momento, independente do backend)
```

Cada fase é deployável de forma independente. As tarefas dentro de cada fase devem ser executadas na ordem listada pois Task 3 depende de Task 2 (DependencyInjection.cs é modificado nas duas).

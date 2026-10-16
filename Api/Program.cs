using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RealEstate.Api.Auth;
using RealEstate.Api.Seeding;
using RealEstate.Domain;
using RealEstate.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// Render indica el puerto por la variable PORT (en local se usan los de launchSettings)
if (Environment.GetEnvironmentVariable("PORT") is { Length: > 0 } port)
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// ---------- Base de datos: PostgreSQL (Supabase, pooler en modo sesión) ----------
var connectionString = config.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("Falta la cadena de conexión: define ConnectionStrings__Default.");

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentTenant, HttpCurrentTenant>();

// AddDbContext (scoped), NO AddDbContextPool: el contexto depende del tenant de cada request
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));

// ---------- Identity ----------
builder.Services.AddIdentityCore<AppUser>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 8;
        o.Password.RequireNonAlphanumeric = false;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
    })
    .AddRoles<AppRole>()
    .AddEntityFrameworkStores<AppDbContext>();

// ---------- JWT ----------
builder.Services.Configure<JwtOptions>(config.GetSection(JwtOptions.Section));
var jwt = config.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
if (Encoding.UTF8.GetByteCount(jwt.Key) < 32)
    throw new InvalidOperationException("Jwt__Key debe tener al menos 32 caracteres.");

builder.Services.AddSingleton<JwtTokenService>();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false; // los claims conservan su nombre: "sub", "role", "tenant_id"
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = "sub",
            RoleClaimType = "role"
        };
    });
builder.Services.AddAuthorization();

// ---------- CORS: solo el panel Blazor (la app móvil no lo necesita) ----------
var origins = config.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));

// ---------- Límite de intentos de login por IP ----------
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));

    // Catálogo público: más holgado que el login
    o.AddPolicy("public", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) }));
});

// ---------- MVC + imágenes + seed ----------
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.Configure<SeedOptions>(config.GetSection(SeedOptions.Section));
builder.Services.AddImageHandling(config);
builder.Services.AddHttpClient();   // IHttpClientFactory (lo usa la vista previa para compartir)

var app = builder.Build();

// Migraciones + roles + SuperAdmin (+ datos demo). Si falla, la API arranca igual y lo deja en el log
await DbSeeder.InitializeAsync(app.Services);

// Render termina el HTTPS: no se usa UseHttpsRedirection. Los encabezados X-Forwarded-* los procesa
// ASPNETCORE_FORWARDEDHEADERS_ENABLED=true (definido en el Dockerfile) para ver la IP real del cliente.
app.UseCors();
app.UseRateLimiter();
app.UseLocalMedia();
app.UseAuthentication();
app.UseAuthorization();

// Liviano: úsalo para despertar la instancia de Render sin tocar la base de datos
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Comprueba además la conexión a PostgreSQL (útil para ver si Supabase está pausado)
app.MapGet("/health/db", async (AppDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct)
        ? Results.Ok(new { db = "ok" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

app.MapControllers();

app.Run();

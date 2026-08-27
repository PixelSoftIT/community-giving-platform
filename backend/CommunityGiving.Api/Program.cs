using System.Text;
using AspNetCoreRateLimit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using CommunityGiving.Api.Data;
using CommunityGiving.Api.Models;
using CommunityGiving.Api.Services;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// ---------- Port binding ----------
// Platforms like Render assign a port dynamically via the PORT env var and route traffic
// to it. Our own docker-compose (Contabo/Oracle) doesn't set PORT, so this falls back to
// 8080 there, matching the Dockerfile's EXPOSE. Must bind 0.0.0.0, not localhost, so the
// platform's proxy (outside the container) can reach it.
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// ---------- Database (PostgreSQL) ----------
// Render (and some other PaaS platforms) hand out the connection string as a
// postgres://user:pass@host:port/db URI, but Npgsql expects the keyword=value format
// (Host=...;Username=...). Normalize either form here so it works unmodified in both
// this docker-compose setup and on Render, without the person needing to hand-convert it.
static string NormalizeConnectionString(string raw)
{
    if (!raw.StartsWith("postgres://") && !raw.StartsWith("postgresql://"))
        return raw; // already in keyword=value format

    var uri = new Uri(raw);
    var userInfo = uri.UserInfo.Split(':', 2);
    var csb = new NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.Port > 0 ? uri.Port : 5432,
        Username = Uri.UnescapeDataString(userInfo[0]),
        Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "",
        Database = uri.AbsolutePath.TrimStart('/'),
        // "Prefer" rather than "Require": uses SSL whenever the server offers it (true for
        // virtually all managed Postgres, including Render's), but won't hard-fail the
        // connection on an internal/private network that doesn't happen to offer it.
        SslMode = SslMode.Prefer,
        TrustServerCertificate = true
    };
    return csb.ConnectionString;
}

var rawConnectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");
var connectionString = NormalizeConnectionString(rawConnectionString);

builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));

// ---------- Identity (password hashing, lockout, roles) ----------
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    // Strong password policy — adjust to your congregation's needs, but don't weaken below this.
    options.Password.RequiredLength = 10;
    options.Password.RequireDigit = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;

    // Brute-force protection
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// ---------- JWT Authentication ----------
var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSection["Issuer"],
        ValidAudience = jwtSection["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["Key"]!)),
        ClockSkew = TimeSpan.FromMinutes(2)
    };
});

builder.Services.AddAuthorization();

// ---------- App services ----------
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IStripeService, StripeService>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<ISmsSender, TwilioSmsSender>();
builder.Services.AddSingleton<IPdfDocumentService, PdfDocumentService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IAuditService, AuditService>();

// ---------- CORS: only allow the known frontend origin(s) ----------
var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

// ---------- Rate limiting (protects login/payment endpoints from abuse) ----------
builder.Services.AddMemoryCache();
builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("IpRateLimiting"));
builder.Services.AddInMemoryRateLimiting();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    // We're always behind a reverse proxy (Render's edge, or our own nginx on Contabo/Oracle)
    // that terminates TLS and forwards the original scheme/IP via these headers. Without this,
    // UseHttpsRedirection can't tell the original request was already HTTPS and may redirect
    // loop, and rate limiting/logging would see the proxy's IP instead of the real client's.
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // The proxy's IP isn't known ahead of time on a PaaS platform, so trust any proxy here —
    // acceptable since inbound traffic can only reach us through that trusted edge anyway.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Create the database schema directly from the current model on startup, rather than
// using versioned EF Core migration files (those require running `dotnet ef migrations
// add` with the .NET SDK — a separate one-time setup step not done for this project).
// Simpler to operate, at the cost of not having incremental migration history: if the
// data model changes after the database already has real data in it, this won't apply
// the change automatically — that's the point where switching to proper EF Core
// migrations becomes worth the setup.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    // EnsureCreatedAsync() only builds the schema when the target DATABASE itself doesn't
    // exist yet. On managed hosts like Render, the database is always pre-provisioned
    // (even though it's empty) before the app ever connects — so that check silently
    // no-ops and leaves zero tables behind. Check for a known table directly instead, and
    // build the schema ourselves the first time it's missing; skip on every later restart.
    // Using the raw ADO.NET connection directly here (rather than EF Core's SqlQueryRaw)
    // to avoid EF's own column-naming conventions for raw scalar queries — simpler and
    // more predictable than fighting that mapping layer for a single yes/no check.
    var connection = db.Database.GetDbConnection();
    if (connection.State != System.Data.ConnectionState.Open)
        await connection.OpenAsync();

    bool schemaExists;
    await using (var checkCommand = connection.CreateCommand())
    {
        checkCommand.CommandText =
            "SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'AspNetRoles')";
        schemaExists = (bool)(await checkCommand.ExecuteScalarAsync())!;
    }
    await connection.CloseAsync();

    if (!schemaExists)
    {
        var createScript = db.Database.GenerateCreateScript();
        await db.Database.ExecuteSqlRawAsync(createScript);
    }

    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    // Admin: full access. Treasurer: finance module (invoices, expenses, income, receipts).
    // Secretary: meetings and notifications. ProgramCoordinator: program terms, sibling
    // discount rules, and student registrations — separate from general Admin access so an
    // org can hand this off to whoever runs their program without granting full admin rights.
    // Member: self-service portal only.
    foreach (var role in new[] { "Admin", "Treasurer", "Secretary", "ProgramCoordinator", "Member" })
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));

    // Seed a single default OrganizationSettings row if none exists yet — the admin
    // can then change name/type/branding from the Settings tab without a redeploy.
    if (!await db.OrganizationSettings.AnyAsync())
    {
        db.OrganizationSettings.Add(new OrganizationSettings());
        await db.SaveChangesAsync();
    }

    // Seed a default Prep-Year 12 level list if none exists yet — a sensible starting point
    // for a school-style program; admins can rename/add/remove these freely afterward, and
    // orgs running a different kind of program can replace the list entirely.
    if (!await db.ProgramLevels.AnyAsync())
    {
        var defaultLevels = new List<string> { "Prep" };
        for (var year = 1; year <= 12; year++) defaultLevels.Add($"Year {year}");
        for (var i = 0; i < defaultLevels.Count; i++)
            db.ProgramLevels.Add(new ProgramLevel { Name = defaultLevels[i], SortOrder = i });
        await db.SaveChangesAsync();
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ---------- Security middleware ----------
app.UseForwardedHeaders(); // must run before UseHttpsRedirection so it sees the real original scheme
app.UseHttpsRedirection();
app.UseHsts(); // enforce HTTPS on the browser side (the platform's edge/nginx terminates TLS in front of this)

// Unauthenticated health check for the hosting platform's uptime monitor (Render's
// healthCheckPath, or any load balancer) — deliberately does nothing but confirm the
// process is alive and able to respond.
app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();

app.Use(async (context, next) =>
{
    // Defense-in-depth security headers (nginx also sets some of these — harmless to duplicate)
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.Append("Permissions-Policy", "geolocation=(), microphone=(), camera=()");
    await next();
});

app.UseIpRateLimiting();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

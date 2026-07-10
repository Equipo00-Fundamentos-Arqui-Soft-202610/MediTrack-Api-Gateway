using System.Text;
using MediTrackApiGateway;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using Ocelot.Provider.Polly;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("ocelot.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"ocelot.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("MediTrackClients", policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader());
});

// JWT authentication -- valores reales vía user-secrets en desarrollo, vía
// variables de entorno en producción. Nunca en appsettings.json (ver README).
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.Key), "Jwt:Key es obligatorio")
    .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer), "Jwt:Issuer es obligatorio")
    .Validate(o => !string.IsNullOrWhiteSpace(o.Audience), "Jwt:Audience es obligatorio")
    .ValidateOnStart();

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Falta la sección 'Jwt' en la configuración.");

builder.Services.AddAuthentication()
    .AddJwtBearer("MediTrackBearer", options =>
    {
        // Keep original claim names so Ocelot role authorization can read "role"
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddOcelot(builder.Configuration).AddPolly();

var app = builder.Build();

app.UseCors("MediTrackClients");

app.UseAuthentication();

// Ocelot's rate limiter requires a ClientId header to bucket requests per client --
// it has no built-in IP fallback (by design, see https://ocelot.readthedocs.io/en/latest/features/ratelimiting.html).
// This assigns ClientId = caller IP before the rate limiting middleware runs, so
// per-client throttling works without requiring API keys from Web/Mobile.
var pipelineConfiguration = new Ocelot.Middleware.OcelotPipelineConfiguration
{
    PreErrorResponderMiddleware = async (context, next) =>
    {
        if (!context.Request.Headers.ContainsKey("ClientId"))
        {
            var forwardedFor = context.Request.Headers["X-Forwarded-For"].ToString();
            var clientIp = !string.IsNullOrWhiteSpace(forwardedFor)
                ? forwardedFor.Split(',')[0].Trim()
                : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            context.Request.Headers["ClientId"] = clientIp;
        }

        await next.Invoke();
    }
};

await app.UseOcelot(pipelineConfiguration);

app.Run();

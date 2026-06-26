using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("ocelot.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"ocelot.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

// JWT authentication: el Gateway es el punto principal de validación y autorización
// por rol (CON-04, AC-01). Las rutas de ocelot.json referencian este esquema mediante
// AuthenticationProviderKey = "MediTrackBearer". El secreto y el issuer/audience deben
// coincidir con los del Identity Service que emite los tokens.
var jwtSection = builder.Configuration.GetSection("Jwt");
var signingKey = jwtSection["Key"]
    ?? throw new InvalidOperationException("Falta la clave de firma JWT en 'Jwt:Key'.");

builder.Services.AddAuthentication()
    .AddJwtBearer("MediTrackBearer", options =>
    {
        // Mantener los claims con su nombre original ("role" en vez del URI largo de
        // Microsoft) para que RouteClaimsRequirement de Ocelot pueda autorizar por rol.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtSection["Audience"],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddOcelot(builder.Configuration);

var app = builder.Build();

app.UseCors("AllowAll");

await app.UseOcelot();

app.Run();

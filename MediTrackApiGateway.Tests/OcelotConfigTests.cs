using System.Runtime.CompilerServices;
using System.Text.Json;
using Xunit;

namespace MediTrackApiGateway.Tests;

/// Cubre la causa raíz del incidente de 429 en producción: ocelot.json y
/// ocelot.Production.json se fusionan por índice posicional de array (ASP.NET
/// Core config chaining), así que un desalineamiento entre ambos archivos hace
/// que rutas de un archivo "se corran" sobre las del otro en runtime sin que
/// haya ningún error de compilación o de arranque que lo delate.
public class OcelotConfigTests
{
    private static readonly string GatewayProjectDir = ResolveGatewayProjectDir();

    private static string ResolveGatewayProjectDir([CallerFilePath] string testFilePath = "")
    {
        var testsDir = Path.GetDirectoryName(testFilePath)!;
        return Path.GetFullPath(Path.Combine(testsDir, "..", "MediTrackApiGateway"));
    }

    private static JsonElement LoadRoutes(string fileName)
    {
        var path = Path.Combine(GatewayProjectDir, fileName);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("Routes").Clone();
    }

    private static string[] RouteKeys(JsonElement routes) =>
        routes.EnumerateArray().Select(r => r.GetProperty("Key").GetString()!).ToArray();

    [Fact]
    public void Production_and_base_configs_have_the_same_number_of_routes()
    {
        var baseRoutes = LoadRoutes("ocelot.json");
        var prodRoutes = LoadRoutes("ocelot.Production.json");

        Assert.Equal(baseRoutes.GetArrayLength(), prodRoutes.GetArrayLength());
    }

    [Fact]
    public void Production_and_base_configs_have_routes_in_the_same_order()
    {
        // ASP.NET Core's configuration chaining overlays JSON files by
        // positional array index ("Routes:0", "Routes:1", ...), not by
        // matching "Key". If the two files list routes in different order,
        // a route in one file silently takes on the Host/RateLimit of
        // whatever sits at the same index in the other file.
        var baseKeys = RouteKeys(LoadRoutes("ocelot.json"));
        var prodKeys = RouteKeys(LoadRoutes("ocelot.Production.json"));

        Assert.Equal(baseKeys, prodKeys);
    }

    [Theory]
    [InlineData("followup-compliance-pending-validation")]
    [InlineData("followup-compliance-video")]
    [InlineData("followup-compliance-approve")]
    [InlineData("followup-compliance-reject")]
    public void Specific_validator_route_exists_in_both_configs(string key)
    {
        Assert.Contains(key, RouteKeys(LoadRoutes("ocelot.json")));
        Assert.Contains(key, RouteKeys(LoadRoutes("ocelot.Production.json")));
    }

    [Theory]
    [InlineData("ocelot.json")]
    [InlineData("ocelot.Production.json")]
    public void Specific_validator_route_outranks_the_generic_followup_catchall(string fileName)
    {
        var routes = LoadRoutes(fileName);
        var followupGeneric = routes.EnumerateArray().Single(r => r.GetProperty("Key").GetString() == "followup");
        var pendingValidation = routes.EnumerateArray()
            .Single(r => r.GetProperty("Key").GetString() == "followup-compliance-pending-validation");

        var genericPriority = followupGeneric.TryGetProperty("Priority", out var p) ? p.GetInt32() : 0;
        var specificPriority = pendingValidation.GetProperty("Priority").GetInt32();

        Assert.True(specificPriority > genericPriority,
            $"La ruta específica (Priority={specificPriority}) debe superar a la genérica '/followup/{{everything}}' (Priority={genericPriority}) para no caer en su rate limit compartido.");
    }

    [Theory]
    [InlineData("ocelot.json")]
    [InlineData("ocelot.Production.json")]
    public void Specific_validator_routes_keep_TechnicalStaff_authorization(string fileName)
    {
        var routes = LoadRoutes(fileName);
        var restrictedKeys = new[]
        {
            "followup-compliance-pending-validation",
            "followup-compliance-video",
            "followup-compliance-approve",
            "followup-compliance-reject",
        };

        foreach (var key in restrictedKeys)
        {
            var route = routes.EnumerateArray().Single(r => r.GetProperty("Key").GetString() == key);

            Assert.Equal("MediTrackBearer",
                route.GetProperty("AuthenticationOptions").GetProperty("AuthenticationProviderKey").GetString());
            Assert.Equal("TechnicalStaff",
                route.GetProperty("RouteClaimsRequirement").GetProperty("role").GetString());
        }
    }

    [Theory]
    [InlineData("ocelot.json")]
    [InlineData("ocelot.Production.json")]
    public void Specific_validator_routes_have_their_own_isolated_rate_limit(string fileName)
    {
        // Deben tener un límite propio para no compartir bucket con el resto
        // del tráfico /followup/* (p. ej. el polling de estado de Mobile),
        // que fue la causa del 429 al caer en la ruta catch-all genérica.
        var routes = LoadRoutes(fileName);
        var keys = new[]
        {
            "followup-compliance-pending-validation",
            "followup-compliance-video",
            "followup-compliance-approve",
            "followup-compliance-reject",
        };

        foreach (var key in keys)
        {
            var route = routes.EnumerateArray().Single(r => r.GetProperty("Key").GetString() == key);
            var rateLimit = route.GetProperty("RateLimitOptions");

            Assert.True(rateLimit.GetProperty("EnableRateLimiting").GetBoolean());
            Assert.True(rateLimit.GetProperty("Limit").GetInt32() > 0);
        }
    }

    [Fact]
    public void Production_config_points_validator_routes_at_the_real_followup_host()
    {
        var routes = LoadRoutes("ocelot.Production.json");
        var pendingValidation = routes.EnumerateArray()
            .Single(r => r.GetProperty("Key").GetString() == "followup-compliance-pending-validation");

        var host = pendingValidation.GetProperty("DownstreamHostAndPorts")[0].GetProperty("Host").GetString();
        Assert.Equal("meditrack-followup-service.onrender.com", host);
    }
}

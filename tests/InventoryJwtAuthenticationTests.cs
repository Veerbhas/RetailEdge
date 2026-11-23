using System.Security.Claims;
using System.Security.Cryptography;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RetailEdge.Inventory;
using RetailEdge.Security;
using Xunit;

namespace RetailEdge.Tests;

public sealed class InventoryJwtAuthenticationTests
{
    [Theory]
    [InlineData("RS256")]
    [InlineData("HS256")]
    public void RegistrationPinsAlgorithmAndRequiresAllLifetimeClaims(string algorithm)
    {
        using ServiceProvider serviceProvider = CreateServices(algorithm);
        JwtBearerOptions options = serviceProvider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        TokenValidationParameters parameters = options.TokenValidationParameters;

        Assert.Equal([algorithm], parameters.ValidAlgorithms);
        Assert.True(parameters.ValidateIssuerSigningKey);
        Assert.True(parameters.RequireSignedTokens);
        Assert.True(parameters.ValidateIssuer);
        Assert.True(parameters.ValidateAudience);
        Assert.True(parameters.ValidateLifetime);
        Assert.True(parameters.RequireExpirationTime);

        bool lifetimeValidator(
            DateTime? notBefore,
            DateTime? expires,
            SecurityToken? token) =>
            parameters.LifetimeValidator!(
                notBefore,
                expires,
                token,
                parameters);

        DateTime now = DateTime.UtcNow;
        var validToken = new JwtSecurityToken(
            claims:
            [
                new Claim(
                    "iat",
                    new DateTimeOffset(now).ToUnixTimeSeconds().ToString())
            ]);
        var futureIssuedToken = new JwtSecurityToken(
            claims:
            [
                new Claim(
                    "iat",
                    new DateTimeOffset(now.AddMinutes(2)).ToUnixTimeSeconds().ToString())
            ]);
        var tokenWithoutIssuedAt = new JwtSecurityToken();

        Assert.True(lifetimeValidator(
            now.AddMinutes(-1),
            now.AddMinutes(5),
            validToken));
        Assert.False(lifetimeValidator(
            null,
            now.AddMinutes(5),
            validToken));
        Assert.False(lifetimeValidator(
            now.AddMinutes(-1),
            null,
            validToken));
        Assert.False(lifetimeValidator(
            now.AddMinutes(-1),
            now.AddMinutes(5),
            futureIssuedToken));
        Assert.False(lifetimeValidator(
            now.AddMinutes(-1),
            now.AddMinutes(5),
            tokenWithoutIssuedAt));
        Assert.False(lifetimeValidator(
            now.AddMinutes(-1),
            now.AddMinutes(-1),
            validToken));
    }

    [Fact]
    public void RegistrationRejectsUnpinnedOrUnsupportedAlgorithm()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Jwt:Algorithm"] = "none",
                ["Authentication:Jwt:Issuer"] = "retail-edge",
                ["Authentication:Jwt:Audience"] = "retail-edge-api"
            })
            .Build();

        Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection()
                .AddRetailEdgeJwtAuthentication(configuration));
    }

    [Fact]
    public void JwtValidationRejectsUnsignedExpiredAndFutureIssuedTokens()
    {
        using ServiceProvider serviceProvider = CreateServices("HS256");
        JwtBearerOptions options = serviceProvider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        TokenValidationParameters parameters = options.TokenValidationParameters;
        var handler = new JwtSecurityTokenHandler();
        var signingCredentials = new SigningCredentials(
            parameters.IssuerSigningKey,
            SecurityAlgorithms.HmacSha256);
        DateTime now = DateTime.UtcNow;

        string validToken = CreateToken(
            parameters,
            now.AddMinutes(-1),
            now.AddMinutes(5),
            now,
            signingCredentials);
        ClaimsPrincipal principal = handler.ValidateToken(
            validToken,
            parameters,
            out _);
        Assert.True(principal.Identity?.IsAuthenticated);

        string unsignedToken = CreateToken(
            parameters,
            now.AddMinutes(-1),
            now.AddMinutes(5),
            now,
            signingCredentials: null);
        Assert.ThrowsAny<SecurityTokenException>(
            () => handler.ValidateToken(unsignedToken, parameters, out _));

        string expiredToken = CreateToken(
            parameters,
            now.AddMinutes(-10),
            now.AddMinutes(-5),
            now.AddMinutes(-10),
            signingCredentials);
        Assert.ThrowsAny<SecurityTokenException>(
            () => handler.ValidateToken(expiredToken, parameters, out _));

        string futureIssuedToken = CreateToken(
            parameters,
            now,
            now.AddMinutes(5),
            now.AddMinutes(2),
            signingCredentials);
        Assert.ThrowsAny<SecurityTokenException>(
            () => handler.ValidateToken(futureIssuedToken, parameters, out _));
    }

    [Fact]
    public void RsaValidationAcceptsOnlyRs256Signature()
    {
        using RSA rsa = RSA.Create(2048);
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Jwt:Algorithm"] = "RS256",
                ["Authentication:Jwt:Issuer"] = "retail-edge",
                ["Authentication:Jwt:Audience"] = "retail-edge-api",
                ["Authentication:Jwt:RsaPublicKeyPem"] =
                    rsa.ExportSubjectPublicKeyInfoPem()
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRetailEdgeJwtAuthentication(configuration);
        using ServiceProvider serviceProvider = services.BuildServiceProvider();
        TokenValidationParameters parameters = serviceProvider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme)
            .TokenValidationParameters;
        DateTime now = DateTime.UtcNow;
        var handler = new JwtSecurityTokenHandler();
        string validToken = CreateToken(
            parameters,
            now.AddMinutes(-1),
            now.AddMinutes(5),
            now,
            new SigningCredentials(
                new RsaSecurityKey(rsa),
                SecurityAlgorithms.RsaSha256));

        Assert.True(handler.ValidateToken(validToken, parameters, out _).Identity?.IsAuthenticated);

        string substitutedAlgorithmToken = CreateToken(
            parameters,
            now.AddMinutes(-1),
            now.AddMinutes(5),
            now,
            new SigningCredentials(
                new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)),
                SecurityAlgorithms.HmacSha256));
        Assert.ThrowsAny<SecurityTokenException>(
            () => handler.ValidateToken(substitutedAlgorithmToken, parameters, out _));
    }

    private static ServiceProvider CreateServices(string algorithm)
    {
        var values = new Dictionary<string, string?>
        {
            ["Authentication:Jwt:Algorithm"] = algorithm,
            ["Authentication:Jwt:Issuer"] = "retail-edge",
            ["Authentication:Jwt:Audience"] = "retail-edge-api"
        };

        if (algorithm == "RS256")
        {
            using RSA rsa = RSA.Create(2048);
            values["Authentication:Jwt:RsaPublicKeyPem"] =
                rsa.ExportSubjectPublicKeyInfoPem();
        }
        else
        {
            values["Authentication:Jwt:HmacSigningKeyBase64"] =
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        }

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRetailEdgeJwtAuthentication(configuration);
        return services.BuildServiceProvider();
    }

    private static string CreateToken(
        TokenValidationParameters parameters,
        DateTime notBefore,
        DateTime expires,
        DateTime issuedAt,
        SigningCredentials? signingCredentials)
    {
        var token = new JwtSecurityToken(
            parameters.ValidIssuer,
            parameters.ValidAudience,
            [
                new Claim(
                    "iat",
                    new DateTimeOffset(issuedAt)
                        .ToUnixTimeSeconds()
                        .ToString())
            ],
            notBefore,
            expires,
            signingCredentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.JsonWebTokens;

namespace RetailEdge.Security;

public static class RetailEdgeAuthenticationExtensions
{
    private static readonly TimeSpan AllowedClockSkew = TimeSpan.FromSeconds(30);

    public static IServiceCollection AddRetailEdgeJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection jwtConfiguration =
            configuration.GetSection("Authentication:Jwt");
        string algorithm = RequiredSetting(jwtConfiguration, "Algorithm");
        string issuer = RequiredSetting(jwtConfiguration, "Issuer");
        string audience = RequiredSetting(jwtConfiguration, "Audience");
        SecurityKey signingKey = CreateSigningKey(jwtConfiguration, algorithm);

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.SaveToken = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey,
                    RequireSignedTokens = true,
                    ValidAlgorithms = [algorithm],
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = AllowedClockSkew,
                    LifetimeValidator = ValidateLifetime
                };
            });

        services.AddAuthorization();
        return services;
    }

    private static SecurityKey CreateSigningKey(
        IConfigurationSection configuration,
        string algorithm)
    {
        if (algorithm == SecurityAlgorithms.RsaSha256)
        {
            string publicKeyPem = RequiredSetting(configuration, "RsaPublicKeyPem");
            RSA rsa = RSA.Create();
            try
            {
                rsa.ImportFromPem(publicKeyPem);
            }
            catch (Exception exception)
                when (exception is ArgumentException or CryptographicException)
            {
                rsa.Dispose();
                throw new InvalidOperationException(
                    "The configured RS256 public key is not valid PEM.",
                    exception);
            }

            if (rsa.KeySize < 2048)
            {
                rsa.Dispose();
                throw new InvalidOperationException(
                    "The configured RS256 public key must be at least 2048 bits.");
            }

            return new RsaSecurityKey(rsa);
        }

        if (algorithm == SecurityAlgorithms.HmacSha256)
        {
            string encodedKey = RequiredSetting(configuration, "HmacSigningKeyBase64");
            byte[] keyBytes;
            try
            {
                keyBytes = Convert.FromBase64String(encodedKey);
            }
            catch (FormatException exception)
            {
                throw new InvalidOperationException(
                    "The configured HS256 signing key must be valid Base64.",
                    exception);
            }

            if (keyBytes.Length < 32)
            {
                throw new InvalidOperationException(
                    "The configured HS256 signing key must contain at least 32 bytes.");
            }

            return new SymmetricSecurityKey(keyBytes);
        }

        throw new InvalidOperationException(
            "JWT algorithm must be exactly RS256 or HS256.");
    }

    private static bool ValidateLifetime(
        DateTime? notBefore,
        DateTime? expires,
        Microsoft.IdentityModel.Tokens.SecurityToken? token,
        TokenValidationParameters validationParameters)
    {
        DateTime now = DateTime.UtcNow;
        TimeSpan clockSkew = validationParameters.ClockSkew;

        if (!notBefore.HasValue
            || !expires.HasValue
            || notBefore.Value > now.Add(clockSkew)
            || expires.Value <= now.Subtract(clockSkew)
            || token is null)
        {
            return false;
        }

        IEnumerable<System.Security.Claims.Claim>? claims = token switch
        {
            JwtSecurityToken jwtSecurityToken => jwtSecurityToken.Claims,
            JsonWebToken jsonWebToken => jsonWebToken.Claims,
            _ => null
        };
        string? issuedAt = claims?
            .FirstOrDefault(claim => claim.Type == "iat")
            ?.Value;
        if (!long.TryParse(
                issuedAt,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long issuedAtSeconds))
        {
            return false;
        }

        try
        {
            DateTime issuedAtUtc = DateTimeOffset
                .FromUnixTimeSeconds(issuedAtSeconds)
                .UtcDateTime;
            return issuedAtUtc <= now.Add(clockSkew)
                && issuedAtUtc < expires.Value;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static string RequiredSetting(
        IConfiguration configuration,
        string key)
    {
        string? value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Required JWT configuration value '{key}' is missing.");
        }

        return value;
    }
}

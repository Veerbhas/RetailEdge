using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RetailEdge.Cart;
using RetailEdge.Inventory;
using RetailEdge.Security;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

string signingKey = builder.Configuration["Authentication:Jwt:HmacSigningKeyBase64"]
    ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

var jwtSettings = new Dictionary<string, string?>
{
    ["Authentication:Jwt:Algorithm"] = "HS256",
    ["Authentication:Jwt:Issuer"] =
        builder.Configuration["Authentication:Jwt:Issuer"] ?? "retail-edge",
    ["Authentication:Jwt:Audience"] =
        builder.Configuration["Authentication:Jwt:Audience"] ?? "retail-edge-api",
    ["Authentication:Jwt:HmacSigningKeyBase64"] = signingKey
};
builder.Configuration.AddInMemoryCollection(jwtSettings);

string connectionString =
    builder.Configuration.GetConnectionString("RetailEdge")
    ?? "Data Source=retail-edge.db";

builder.Services.AddRetailEdgeJwtAuthentication(builder.Configuration);
builder.Services.AddInventory(connectionString);
builder.Services.AddCart(connectionString);
builder.Services.AddControllers();

WebApplication app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

await app.Services.MigrateInventoryDatabaseAsync();
await app.Services.MigrateCartDatabaseAsync();

await app.RunAsync();

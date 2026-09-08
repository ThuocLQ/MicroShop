using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading.RateLimiting;
using ApiGateway;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using MicroShop.ServiceDefaults.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddControllers();
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier;
        context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.Request.Headers["X-Correlation-ID"].FirstOrDefault()
            ?? context.HttpContext.TraceIdentifier;
    };
});

var gatewayOptions = builder.Configuration
    .GetSection(GatewayOptions.SectionName)
    .Get<GatewayOptions>()
    ?? new GatewayOptions();

var jwtSecretKey = builder.Configuration["Jwt:SecretKey"];
if (!builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(jwtSecretKey))
{
    throw new InvalidOperationException("Jwt:SecretKey is required outside Development because protected gateway routes are enabled.");
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("GatewayCors", policy =>
    {
        if (gatewayOptions.AllowedCorsOrigins.Length > 0)
        {
            policy.WithOrigins(gatewayOptions.AllowedCorsOrigins);
        }
        else if (builder.Environment.IsDevelopment())
        {
            policy.AllowAnyOrigin();
        }

        policy
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        await Results.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Too many requests",
                type: "https://microshop.dev/problems/rate-limit-exceeded",
                detail: "Retry after the current rate-limit window has elapsed.")
            .ExecuteAsync(context.HttpContext);
    };
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var category = GetRateLimitCategory(context.Request.Path);
        var clientKey = GetClientKey(context);
        var permitLimit = category switch
        {
            "auth" => Math.Max(1, gatewayOptions.AuthPermitLimit),
            "webhook" => Math.Max(1, gatewayOptions.WebhookPermitLimit),
            "health" => Math.Max(1, gatewayOptions.HealthPermitLimit),
            _ => Math.Max(1, gatewayOptions.GeneralPermitLimit)
        };

        return RateLimitPartition.GetFixedWindowLimiter(
            $"{category}:{clientKey}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromSeconds(Math.Max(1, gatewayOptions.WindowSeconds)),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
});

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var issuer = builder.Configuration["Jwt:Issuer"];
        var audience = builder.Configuration["Jwt:Audience"];
        var secretKey = jwtSecretKey;

        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                await ApiProblemResults.Unauthorized().ExecuteAsync(context.HttpContext);
            }
        };

        if (!string.IsNullOrWhiteSpace(secretKey))
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = !string.IsNullOrWhiteSpace(issuer),
                ValidIssuer = issuer,
                ValidateAudience = !string.IsNullOrWhiteSpace(audience),
                ValidAudience = audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1)
            };
        }
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("authenticated", policy => policy.RequireAuthenticatedUser());
});

builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseCorrelationId();
app.UseExceptionHandler();
app.UseSecurityHeaders();
app.UseDebugRouteGuard(gatewayOptions);
app.UseCors("GatewayCors");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Docker"))
{
    app.MapHealthChecks("/health");
}

app.MapGet("/", () => Results.Ok(new
{
    service = "ApiGateway",
    version = "v1",
    status = "running"
}));

app.MapReverseProxy();

app.Run();

static string GetRateLimitCategory(PathString path)
{
    if (path.StartsWithSegments("/auth"))
    {
        return "auth";
    }

    if (path.StartsWithSegments("/webhooks"))
    {
        return "webhook";
    }

    if (path.StartsWithSegments("/health") || path.StartsWithSegments("/alive"))
    {
        return "health";
    }

    return "general";
}

static string GetClientKey(HttpContext context)
{
    // Caddy overwrites this header from Cloudflare before requests enter the private gateway network.
    var clientIpHeader = context.Request.Headers["X-MicroShop-Client-IP"].FirstOrDefault();
    if (IPAddress.TryParse(clientIpHeader, out var clientIp))
    {
        return clientIp.ToString();
    }

    return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

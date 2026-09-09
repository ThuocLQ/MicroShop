using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using NotificationWorker.Application;
using NotificationWorker.Infrastructure;
using NotificationWorker.Infrastructure.Persistence;
using NotificationWorker.Infrastructure.Realtime;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

var jwtSecretKey = builder.Configuration["Jwt:SecretKey"];
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.HttpContext.Request.Path.StartsWithSegments("/realtime"))
                {
                    context.Token = context.Request.Cookies["microshop_access_token"];
                }

                return Task.CompletedTask;
            }
        };

        if (!string.IsNullOrWhiteSpace(jwtSecretKey))
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = !string.IsNullOrWhiteSpace(builder.Configuration["Jwt:Issuer"]),
                ValidIssuer = builder.Configuration["Jwt:Issuer"],
                ValidateAudience = !string.IsNullOrWhiteSpace(builder.Configuration["Jwt:Audience"]),
                ValidAudience = builder.Configuration["Jwt:Audience"],
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1)
            };
        }
    });
builder.Services.AddAuthorization();
builder.Services.AddSignalR();

var app = builder.Build();
app.UseCorrelationId();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();
app.MapHub<CustomerUpdatesHub>("/realtime/customer-events").RequireAuthorization();

await app.InitializeDatabaseAsync();
await app.RunAsync();
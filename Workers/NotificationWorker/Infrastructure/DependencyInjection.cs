using BuildingBlocks.Contracts.Events.Identity;
using BuildingBlocks.Contracts.Events.Orders;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NotificationWorker.Application.Abstractions;
using NotificationWorker.Infrastructure.Identity;
using NotificationWorker.Infrastructure.Messaging;
using NotificationWorker.Infrastructure.Notifications;
using NotificationWorker.Infrastructure.Persistence;
using NotificationWorker.Infrastructure.Realtime;
using NotificationWorker.Application.Realtime;

namespace NotificationWorker.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RabbitMqOptions>().Configure(options =>
        {
            var resolved = RabbitMqOptionsResolver.Resolve(configuration);
            options.Host = resolved.Host;
            options.Port = resolved.Port;
            options.VirtualHost = resolved.VirtualHost;
            options.UserName = resolved.UserName;
            options.Password = resolved.Password;
        }).Validate(
            options => !string.IsNullOrWhiteSpace(options.Host)
                       && options.Port > 0
                       && !string.IsNullOrWhiteSpace(options.UserName)
                       && !string.IsNullOrWhiteSpace(options.Password),
            "RabbitMq configuration is invalid.").ValidateOnStart();

        services.AddOptions<MessageRetryOptions>()
            .Bind(configuration.GetSection(MessageRetryOptions.SectionName))
            .Validate(options => options.RetryCount >= 0 && options.IntervalSeconds > 0, "Messaging retry configuration is invalid.")
            .ValidateOnStart();

        services.AddOptions<NotificationDeliveryOptions>()
            .Bind(configuration.GetSection(NotificationDeliveryOptions.SectionName))
            .Validate(ValidateDeliveryOptions, "NotificationDelivery configuration is invalid.")
            .ValidateOnStart();

        services.AddOptions<NotificationDeliveryRecoveryOptions>()
            .Bind(configuration.GetSection(NotificationDeliveryRecoveryOptions.SectionName))
            .Validate(options => options.ScanIntervalSeconds > 0, "Notification delivery recovery scan interval must be greater than zero.")
            .Validate(options => options.FailureGraceSeconds >= 0, "Notification delivery recovery grace period cannot be negative.")
            .ValidateOnStart();

        var delivery = configuration.GetSection(NotificationDeliveryOptions.SectionName).Get<NotificationDeliveryOptions>()
            ?? new NotificationDeliveryOptions();

        services.AddSingleton<IDatabaseInitializer, PostgresDatabaseInitializer>();
        services.AddSingleton<INotificationDeliveryStore, PostgresNotificationDeliveryStore>();
        services.AddSingleton<ICustomerRealtimeNotifier, CustomerRealtimeNotifier>();
        services.AddPostgresReadinessCheck(configuration, "NotificationDb");
        services.AddRabbitMqReadinessCheck(configuration);

        if (string.Equals(delivery.Mode, "Smtp", StringComparison.OrdinalIgnoreCase))
        {
            var identityBaseUrl = configuration["ServiceUrls:IdentityHttp"]
                ?? throw new InvalidOperationException("ServiceUrls:IdentityHttp is missing for SMTP notifications.");
            services.AddHttpClient<ICustomerContactClient, IdentityCustomerContactClient>(
                client => client.BaseAddress = new Uri(identityBaseUrl.TrimEnd('/') + "/"));
            services.AddScoped<INotificationSender, SmtpNotificationSender>();
        }
        else
        {
            services.AddScoped<INotificationSender, LoggingNotificationSender>();
        }

        services.AddMassTransit(configurator =>
        {
            configurator.AddConsumer<OrderCreatedIntegrationEventConsumer>();
            configurator.AddConsumer<OrderStatusChangedIntegrationEventConsumer>();
            configurator.AddConsumer<CustomerEmailVerificationRequestedIntegrationEventConsumer>();
            configurator.UsingRabbitMq((context, bus) =>
            {
                var rabbit = RabbitMqOptionsResolver.Resolve(configuration);
                bus.Message<OrderCreatedIntegrationEvent>(message => message.SetEntityName("order.created"));
                bus.Message<OrderStatusChangedIntegrationEvent>(message => message.SetEntityName("order.status-changed"));
                bus.Message<CustomerEmailVerificationRequestedIntegrationEvent>(
                    message => message.SetEntityName("identity.email-verification-requested"));
                bus.Host(rabbit.Host, rabbit.Port, rabbit.VirtualHost, host =>
                {
                    host.Username(rabbit.UserName);
                    host.Password(rabbit.Password);
                });

                ConfigureReceiveEndpoint(
                    bus,
                    "notification.email-verification",
                    endpoint => endpoint.ConfigureConsumer<CustomerEmailVerificationRequestedIntegrationEventConsumer>(context),
                    configuration);
                ConfigureReceiveEndpoint(
                    bus,
                    "notification.order-status-changed",
                    endpoint => endpoint.ConfigureConsumer<OrderStatusChangedIntegrationEventConsumer>(context),
                    configuration);
                ConfigureReceiveEndpoint(
                    bus,
                    "notification.order-created",
                    endpoint => endpoint.ConfigureConsumer<OrderCreatedIntegrationEventConsumer>(context),
                    configuration);
            });
        });

        services.AddHostedService<NotificationDeliveryDeadLetterBackgroundService>();
        return services;
    }

    private static void ConfigureReceiveEndpoint(
        IRabbitMqBusFactoryConfigurator bus,
        string queueName,
        Action<IRabbitMqReceiveEndpointConfigurator> configure,
        IConfiguration configuration)
    {
        bus.ReceiveEndpoint(queueName, endpoint =>
        {
            var retry = configuration.GetSection(MessageRetryOptions.SectionName).Get<MessageRetryOptions>()
                ?? new MessageRetryOptions();
            endpoint.UseMessageRetry(policy =>
            {
                policy.Ignore<ArgumentException>();
                policy.Interval(retry.RetryCount, TimeSpan.FromSeconds(retry.IntervalSeconds));
            });
            configure(endpoint);
        });
    }

    private static bool ValidateDeliveryOptions(NotificationDeliveryOptions options)
    {
        if (string.Equals(options.Mode, "Logging", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var user = !string.IsNullOrWhiteSpace(options.Smtp.UserName);
        var password = !string.IsNullOrWhiteSpace(options.Smtp.Password);
        return string.Equals(options.Mode, "Smtp", StringComparison.OrdinalIgnoreCase)
               && !string.IsNullOrWhiteSpace(options.Smtp.Host)
               && options.Smtp.Port is > 0 and <= 65535
               && !string.IsNullOrWhiteSpace(options.Smtp.FromAddress)
               && !string.IsNullOrWhiteSpace(options.Smtp.PublicStorefrontBaseUrl)
               && user == password
               && (!password || !options.Smtp.Password!.Contains("CHANGEME", StringComparison.OrdinalIgnoreCase));
    }
}
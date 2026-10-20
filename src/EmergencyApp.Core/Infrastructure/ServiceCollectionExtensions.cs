using EmergencyApp.Core.Abstractions;
using EmergencyApp.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EmergencyApp.Core.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <param name="seedDemoData">Seeds a demo user and a handful of responders around Newark, NJ.</param>
    /// <param name="options">Dispatch tuning (radius, speeds, how many responders to send).</param>
    public static IServiceCollection AddEmergencyCore(
        this IServiceCollection services, bool seedDemoData = true, DispatchOptions? options = null)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(options ?? new DispatchOptions());

        services.TryAddSingleton<IUserRepository>(_ =>
        {
            var repo = new InMemoryUserRepository();
            if (seedDemoData) repo.Seed(DemoData.Users());
            return repo;
        });
        services.TryAddSingleton<IResponderRepository>(_ =>
        {
            var repo = new InMemoryResponderRepository();
            if (seedDemoData) repo.Seed(DemoData.Responders());
            return repo;
        });
        services.TryAddSingleton<IEmergencyRepository, InMemoryEmergencyRepository>();
        services.TryAddSingleton<IMessageRepository, InMemoryMessageRepository>();
        services.TryAddSingleton<INotificationRepository, InMemoryNotificationRepository>();

        services.TryAddSingleton<IGeocodingService, FakeGeocodingService>();
        services.TryAddSingleton<IRoutingService, StraightLineRoutingService>();
        services.TryAddSingleton<IEmergencyEventPublisher, NullEventPublisher>();
        services.TryAddSingleton<IInstructionProvider, StaticInstructionProvider>();

        services.TryAddSingleton<IDispatchService, DispatchService>();
        services.TryAddSingleton<IEmergencyService, EmergencyService>();
        services.TryAddSingleton<IMessagingService, MessagingService>();
        services.TryAddSingleton<IHomeService, HomeService>();
        services.TryAddSingleton<INotificationService, NotificationService>();
        return services;
    }
}

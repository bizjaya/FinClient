using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Fin;

/// <summary>Dependency injection registration for FinClient.</summary>
public static class FinClientServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="FinClient"/> as a typed client of <c>IHttpClientFactory</c> with the configured options. The
    /// factory's HttpClient timeout is lifted so <see cref="FinClientOptions.Timeout"/> governs each attempt.
    /// </summary>
    public static IHttpClientBuilder AddFinClient(this IServiceCollection services, Action<FinClientOptions> configure)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (configure is null) throw new ArgumentNullException(nameof(configure));
        services.Configure(configure);
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<FinClientOptions>>().Value);
        return services.AddHttpClient<FinClient>()
            .ConfigureHttpClient(client => client.Timeout = System.Threading.Timeout.InfiniteTimeSpan);
    }
}

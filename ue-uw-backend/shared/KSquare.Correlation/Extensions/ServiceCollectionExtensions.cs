using KSquare.Correlation.Contracts;
using KSquare.Correlation.Http;
using KSquare.Correlation.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace KSquare.Correlation.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKsCorrelation(this IServiceCollection services)
    {
        services.AddSingleton<ICorrelationContextAccessor, CorrelationContextAccessor>();
        return services;
    }

    public static IApplicationBuilder UseKsCorrelation(this IApplicationBuilder app)
    {
        app.UseMiddleware<CorrelationMiddleware>();
        return app;
    }

    public static IHttpClientBuilder AddKsCorrelationPropagation(this IHttpClientBuilder builder)
    {
        builder.Services.AddTransient<CorrelationPropagationHandler>();
        builder.AddHttpMessageHandler<CorrelationPropagationHandler>();
        return builder;
    }
}

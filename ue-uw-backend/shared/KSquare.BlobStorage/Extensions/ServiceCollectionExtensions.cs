using KSquare.BlobStorage.Configuration;
using KSquare.BlobStorage.Contracts;
using KSquare.BlobStorage.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KSquare.BlobStorage.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKsBlobStorage(this IServiceCollection services, Action<BlobStorageOptions> configure)
    {
        services.AddOptions<BlobStorageOptions>().Configure(configure);

        services.AddSingleton<IBlobStorageConnector>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<BlobStorageOptions>>().Value;
            ValidateOptions(options);

            return options.Provider switch
            {
                BlobProvider.Azure => ActivatorUtilities.CreateInstance<AzureBlobStorageConnector>(sp),
                BlobProvider.LocalFileSystem => ActivatorUtilities.CreateInstance<LocalFileSystemConnector>(sp),
                _ => throw new InvalidOperationException($"Unsupported blob provider: {options.Provider}")
            };
        });

        return services;
    }

    private static void ValidateOptions(BlobStorageOptions options)
    {
        if (options.Provider == BlobProvider.Azure)
        {
            if (string.IsNullOrWhiteSpace(options.ConnectionString) && string.IsNullOrWhiteSpace(options.AccountName))
            {
                throw new InvalidOperationException("Azure BlobStorage provider requires ConnectionString or AccountName.");
            }
        }

        if (options.Provider == BlobProvider.LocalFileSystem)
        {
            if (string.IsNullOrWhiteSpace(options.LocalRootPath))
            {
                throw new InvalidOperationException("LocalFileSystem BlobStorage provider requires LocalRootPath.");
            }
        }
    }
}

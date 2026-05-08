using KSquare.BlobStorage.Contracts;
using KSquare.EmailSend.Configuration;
using KSquare.EmailSend.Contracts;
using KSquare.EmailSend.Providers;
using KSquare.EmailSend.Templates;
using KSquare.EmailSend.Templates.TemplateLoader;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KSquare.EmailSend.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKsEmailSend(this IServiceCollection services, Action<EmailSendOptions> configure)
    {
        var options = new EmailSendOptions();
        configure(options);

        services.TryAddSingleton(options);

        services.TryAddSingleton<ITemplateLoader>(sp =>
        {
            return options.TemplateSource switch
            {
                EmailTemplateSource.EmbeddedResource => new EmbeddedResourceTemplateLoader(),
                EmailTemplateSource.FileSystem => new FileSystemTemplateLoader(),
                EmailTemplateSource.BlobStorage => new BlobTemplateLoader(options, sp.GetRequiredService<IBlobStorageConnector>()),
                _ => throw new ArgumentOutOfRangeException(nameof(options.TemplateSource), options.TemplateSource, "Unsupported template source.")
            };
        });

        services.TryAddSingleton<IEmailTemplateRenderer, LiquidTemplateRenderer>();

        switch (options.Provider)
        {
            case EmailSendProvider.SendGrid:
                services.TryAddSingleton<IEmailSender, SendGridEmailSender>();
                break;
            case EmailSendProvider.Smtp:
                services.TryAddSingleton<IEmailSender, SmtpEmailSender>();
                break;
            case EmailSendProvider.InMemory:
                services.TryAddSingleton<InMemoryEmailSender>();
                services.TryAddSingleton<IEmailSender>(sp => sp.GetRequiredService<InMemoryEmailSender>());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(options.Provider), options.Provider, "Unsupported email provider.");
        }

        return services;
    }
}

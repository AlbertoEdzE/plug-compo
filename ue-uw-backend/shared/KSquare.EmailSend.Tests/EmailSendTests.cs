using FluentAssertions;
using KSquare.EmailSend.Configuration;
using KSquare.EmailSend.Contracts;
using KSquare.EmailSend.Exceptions;
using KSquare.EmailSend.Extensions;
using KSquare.EmailSend.Models;
using KSquare.EmailSend.Providers;
using KSquare.EmailSend.Tests.Synthesizers;
using Microsoft.Extensions.DependencyInjection;

namespace KSquare.EmailSend.Tests;

public sealed class EmailSendTests
{
    [Fact]
    public async Task SendAsync_stores_email_in_memory_provider()
    {
        var synthesizer = new EmailSendDataSynthesizer();
        var (sender, inMemory) = CreateInMemorySender();

        var msg = new EmailMessage
        {
            From = synthesizer.From(),
            To = [synthesizer.To()],
            Subject = synthesizer.Subject(),
            HtmlBody = synthesizer.HtmlBody(),
            TextBody = null
        };

        var result = await sender.SendAsync(msg);

        result.Success.Should().BeTrue();
        inMemory.SentMessages.Should().ContainSingle();
        inMemory.SentMessages[0].TextBody.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task SendTemplatedAsync_renders_template_tokens()
    {
        var (sender, inMemory) = CreateInMemorySender();

        var to = new EmailAddress("broker@example.com", "Broker");
        var model = new
        {
            SubmissionNumber = "SUB-0042",
            BrokerName = "Jane Smith",
            OldStatus = "Draft",
            NewStatus = "Submitted",
            PortalUrl = "https://uw.company.com/submissions/0042"
        };

        var result = await sender.SendTemplatedAsync("submission-status-changed", model, to);

        result.Success.Should().BeTrue();
        inMemory.SentMessages.Should().ContainSingle();

        var sent = inMemory.SentMessages[0];
        sent.To.Should().ContainSingle(a => a.Address == "broker@example.com");
        sent.HtmlBody.Should().Contain("Jane Smith");
        sent.HtmlBody.Should().Contain("SUB-0042");
    }

    [Fact]
    public async Task Attachment_with_content_is_preserved_in_message()
    {
        var synthesizer = new EmailSendDataSynthesizer();
        var (sender, inMemory) = CreateInMemorySender();

        var attachmentBytes = synthesizer.AttachmentBytes();

        await sender.SendAsync(new EmailMessage
        {
            From = synthesizer.From(),
            To = [synthesizer.To()],
            Subject = synthesizer.Subject(),
            HtmlBody = synthesizer.HtmlBody(),
            Attachments =
            [
                new EmailAttachmentRef
                {
                    FileName = "Quote.pdf",
                    ContentType = "application/pdf",
                    Content = attachmentBytes
                }
            ]
        });

        inMemory.SentMessages.Should().ContainSingle();
        inMemory.SentMessages[0].Attachments.Should().ContainSingle();
        inMemory.SentMessages[0].Attachments[0].Content.Should().BeEquivalentTo(attachmentBytes);
    }

    [Fact]
    public async Task Missing_template_throws_EmailTemplateNotFoundException()
    {
        var (sender, _) = CreateInMemorySender();

        var act = async () => await sender.SendTemplatedAsync("missing-template", new { X = 1 }, new EmailAddress("a@b.com"));

        await act.Should().ThrowAsync<EmailTemplateNotFoundException>();
    }

    private static (IEmailSender Sender, InMemoryEmailSender InMemory) CreateInMemorySender()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKsEmailSend(options =>
        {
            options.Provider = EmailSendProvider.InMemory;
            options.TemplateSource = EmailTemplateSource.EmbeddedResource;
            options.DefaultFromAddress = "noreply@company.com";
            options.DefaultFromName = "UW Workbench";
        });

        var sp = services.BuildServiceProvider();
        var sender = sp.GetRequiredService<IEmailSender>();
        var inMemory = sp.GetRequiredService<InMemoryEmailSender>();

        return (sender, inMemory);
    }
}

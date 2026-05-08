using System.Text;
using FluentAssertions;
using KSquare.EmailIngestion.Internal;
using MimeKit;

namespace KSquare.EmailIngestion.Tests;

public sealed class MimeEmailParserTests
{
    [Fact]
    public void Parse_ShouldExtractBasicFields_FromTextEmail()
    {
        var now = DateTimeOffset.UtcNow;
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Sender Name", "sender@example.com"));
        message.To.Add(new MailboxAddress(string.Empty, "to@example.com"));
        message.Subject = "Test Subject";
        message.Date = now;

        var builder = new BodyBuilder
        {
            TextBody = "Hello from text body."
        };
        message.Body = builder.ToMessageBody();

        byte[] raw;
        using (var ms = new MemoryStream())
        {
            message.WriteTo(ms);
            raw = ms.ToArray();
        }

        var parser = new MimeEmailParser();
        var parsed = parser.Parse(raw);

        parsed.Subject.Should().Be("Test Subject");
        parsed.FromAddress.Should().Be("sender@example.com");
        parsed.FromName.Should().Be("Sender Name");
        parsed.ToAddress.Should().Be("to@example.com");
        parsed.BodyText.Should().Contain("Hello from text body.");
        parsed.Attachments.Should().BeEmpty();
        parsed.ReceivedAt.Should().BeCloseTo(now, TimeSpan.FromSeconds(2));
        parsed.MessageId.Should().NotBeNullOrWhiteSpace();
        parsed.Headers.Should().NotBeNull();
    }

    [Fact]
    public void Parse_ShouldExtractAttachments()
    {
        var now = DateTimeOffset.UtcNow;
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Sender Name", "sender@example.com"));
        message.To.Add(new MailboxAddress(string.Empty, "to@example.com"));
        message.Subject = "With Attachment";
        message.Date = now;

        var builder = new BodyBuilder
        {
            TextBody = "See attachment."
        };
        builder.Attachments.Add("test.txt", Encoding.UTF8.GetBytes("abc"), new ContentType("text", "plain"));
        message.Body = builder.ToMessageBody();

        byte[] raw;
        using (var ms = new MemoryStream())
        {
            message.WriteTo(ms);
            raw = ms.ToArray();
        }

        var parser = new MimeEmailParser();
        var parsed = parser.Parse(raw);

        parsed.Attachments.Should().HaveCount(1);
        parsed.Attachments[0].FileName.Should().Be("test.txt");
        parsed.Attachments[0].ContentType.Should().Be("text/plain");
        parsed.Attachments[0].Content.Should().Equal(Encoding.UTF8.GetBytes("abc"));
    }

    [Fact]
    public void Parse_ShouldFallbackToHtml_WhenTextIsMissing()
    {
        var now = DateTimeOffset.UtcNow;
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Sender Name", "sender@example.com"));
        message.To.Add(new MailboxAddress(string.Empty, "to@example.com"));
        message.Subject = "Html Only";
        message.Date = now;

        var builder = new BodyBuilder
        {
            HtmlBody = "<p>Hello <b>world</b></p>"
        };
        message.Body = builder.ToMessageBody();

        byte[] raw;
        using (var ms = new MemoryStream())
        {
            message.WriteTo(ms);
            raw = ms.ToArray();
        }

        var parser = new MimeEmailParser();
        var parsed = parser.Parse(raw);

        parsed.BodyText.Should().Contain("Hello world");
        parsed.BodyHtml.Should().Contain("<p>");
    }
}

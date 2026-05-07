using System.Text.Json;
using FluentAssertions;
using KSquare.PiiRedaction.Configuration;
using KSquare.PiiRedaction.Contracts;
using KSquare.PiiRedaction.Extensions;
using KSquare.PiiRedaction.Tests.Synthesizers;
using Microsoft.Extensions.DependencyInjection;

namespace KSquare.PiiRedaction.Tests;

public sealed class PiiRedactorTests
{
    [Fact]
    public void IsPiiField_is_case_insensitive()
    {
        var redactor = CreateRedactor();
        redactor.IsPiiField("Email").Should().BeTrue();
    }

    [Fact]
    public void Redacts_deeply_nested_email_field_by_name()
    {
        var synthesizer = new PiiDataSynthesizer();
        var email = synthesizer.Email();
        var redactionToken = "***REDACTED***";

        var redactor = CreateRedactor(options => options.RedactionToken = redactionToken);

        var json = JsonSerializer.Serialize(new
        {
            outer = new
            {
                inner = new
                {
                    email
                }
            }
        });

        var masked = redactor.RedactJson(json);

        masked.Should().Contain(redactionToken);
        masked.Should().NotContain(email);
    }

    [Fact]
    public void Redacts_phone_values_in_multiple_common_formats()
    {
        var synthesizer = new PiiDataSynthesizer();
        var phones = new[]
        {
            synthesizer.PhoneWithFormat("###-###-####"),
            synthesizer.PhoneWithFormat("(###) ###-####"),
            synthesizer.PhoneWithFormat("###.###.####"),
            synthesizer.PhoneWithFormat("+1 ### ### ####"),
        };

        var redactor = CreateRedactor();

        foreach (var phone in phones)
        {
            var json = JsonSerializer.Serialize(new { contactInfo = phone });
            var masked = redactor.RedactJson(json);

            masked.Should().NotContain(phone);
        }
    }

    [Fact]
    public void Redacts_ssn_in_hyphenated_and_numeric_formats()
    {
        var synthesizer = new PiiDataSynthesizer();
        var ssnHyphenated = synthesizer.SsnHyphenated();
        var ssnNumeric = synthesizer.SsnNumeric();

        var redactor = CreateRedactor();

        redactor.RedactValue(ssnHyphenated).Should().NotBe(ssnHyphenated);
        redactor.RedactValue(ssnNumeric).Should().NotBe(ssnNumeric);
    }

    [Fact]
    public void Does_not_modify_non_pii_values()
    {
        var synthesizer = new PiiDataSynthesizer();
        var nonPii = synthesizer.NonPiiString();

        var redactor = CreateRedactor();

        var json = JsonSerializer.Serialize(new { name = nonPii });
        var masked = redactor.RedactJson(json);

        masked.Should().Contain(nonPii);
    }

    [Fact]
    public void Redaction_is_idempotent()
    {
        var synthesizer = new PiiDataSynthesizer();
        var email = synthesizer.Email();

        var redactor = CreateRedactor();

        var json = JsonSerializer.Serialize(new { email });
        var once = redactor.RedactJson(json);
        var twice = redactor.RedactJson(once);

        twice.Should().Be(once);
    }

    [Fact]
    public void Redacts_pii_in_arrays_of_objects()
    {
        var synthesizer = new PiiDataSynthesizer();
        var email1 = synthesizer.Email();
        var email2 = synthesizer.Email();

        var redactor = CreateRedactor();

        var json = JsonSerializer.Serialize(new[]
        {
            new { email = email1 },
            new { email = email2 },
        });

        var masked = redactor.RedactJson(json);

        masked.Should().NotContain(email1);
        masked.Should().NotContain(email2);
    }

    [Fact]
    public void Returns_invalid_json_input_unchanged()
    {
        var synthesizer = new PiiDataSynthesizer();
        var invalidJson = synthesizer.NonPiiString();

        var redactor = CreateRedactor();

        var masked = redactor.RedactJson(invalidJson);

        masked.Should().Be(invalidJson);
    }

    [Fact]
    public void Custom_redaction_token_appears_in_output()
    {
        var synthesizer = new PiiDataSynthesizer();
        var email = synthesizer.Email();
        var token = "[REDACTED]";

        var redactor = CreateRedactor(options => options.RedactionToken = token);

        var masked = redactor.RedactJson(JsonSerializer.Serialize(new { email }));

        masked.Should().Contain(token);
        masked.Should().NotContain(email);
    }

    private static IPiiRedactor CreateRedactor(Action<PiiRedactionOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKsPiiRedaction(configure);

        return services.BuildServiceProvider().GetRequiredService<IPiiRedactor>();
    }
}

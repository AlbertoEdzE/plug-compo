namespace KSquare.PiiRedaction.Configuration;

public class PiiRedactionOptions
{
    public IList<string> PiiFieldNames { get; set; } = new List<string>
    {
        "email", "phone", "mobile", "taxId", "ssn", "ein", "driverLicense",
        "dateOfBirth", "dob", "password", "secret", "creditCard", "bankAccount",
        "routingNumber", "nationalId", "passportNumber", "address", "zipCode"
    };

    public bool DetectEmailPatterns { get; set; } = true;
    public bool DetectPhonePatterns { get; set; } = true;
    public bool DetectSsnPatterns { get; set; } = true;

    public string RedactionToken { get; set; } = "***REDACTED***";
}

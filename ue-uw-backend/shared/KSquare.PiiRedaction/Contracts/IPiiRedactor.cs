namespace KSquare.PiiRedaction.Contracts;

public interface IPiiRedactor
{
    string RedactJson(string json);
    string RedactValue(string value);
    bool IsPiiField(string fieldName);
}

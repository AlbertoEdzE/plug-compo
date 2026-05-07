using KSquare.PiiRedaction.Contracts;

namespace KSquare.PiiRedaction.Serilog;

public static class LoggerConfigurationExtensions
{
    public static global::Serilog.Configuration.LoggerDestructuringConfiguration WithKsPiiRedaction(
        this global::Serilog.Configuration.LoggerDestructuringConfiguration destructure,
        IPiiRedactor redactor
    )
    {
        return destructure.With(new PiiRedactionDestructuringPolicy(redactor));
    }
}

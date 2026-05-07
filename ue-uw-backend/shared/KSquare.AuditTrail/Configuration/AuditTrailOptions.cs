namespace KSquare.AuditTrail.Configuration;

public class AuditTrailOptions
{
    public AuditProvider Provider { get; set; } = AuditProvider.SqlServer;
    public string? ConnectionString { get; set; }
    public string ServiceName { get; set; } = "unknown";
    public bool MaskPiiInBeforeAfter { get; set; } = true;
    public IList<string> PiiFieldNames { get; set; } = ["email", "phone", "taxId", "ssn"];
}

public enum AuditProvider
{
    SqlServer,
    LogAnalytics,
    InMemory
}

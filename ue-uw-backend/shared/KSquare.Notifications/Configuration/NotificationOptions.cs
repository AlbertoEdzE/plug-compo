namespace KSquare.Notifications.Configuration;

public class NotificationOptions
{
    public bool EnableEmail { get; set; } = true;
    public bool EnableInApp { get; set; } = true;
    public bool EnableSms { get; set; } = false;
    public bool EnableTeams { get; set; } = false;

    public IList<string> DefaultChannelsNormal { get; set; } = ["inapp", "email"];
    public IList<string> DefaultChannelsCritical { get; set; } = ["inapp", "email", "sms"];

    public int InAppRetentionDays { get; set; } = 30;
    public TimeSpan DeduplicationWindow { get; set; } = TimeSpan.FromMinutes(5);
    public string? ConnectionString { get; set; }
}

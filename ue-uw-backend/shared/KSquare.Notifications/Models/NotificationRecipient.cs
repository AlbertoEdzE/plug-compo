namespace KSquare.Notifications.Models;

public record NotificationRecipient(
    string UserId,
    string Email,
    string DisplayName,
    IReadOnlyList<string>? OverrideChannels = null
);

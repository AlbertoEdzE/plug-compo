namespace KSquare.EventBus.Models;

public enum OutboxStatus
{
    Pending,
    Delivered,
    Failed,
    DeadLettered
}

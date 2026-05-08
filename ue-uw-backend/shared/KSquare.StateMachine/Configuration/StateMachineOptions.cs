namespace KSquare.StateMachine.Configuration;

public sealed class StateMachineOptions
{
    public StateMachineProvider Provider { get; set; } = StateMachineProvider.Stateless;
    public bool PublishTransitionEvents { get; set; } = true;
    public bool WriteAuditTrail { get; set; } = true;
    public string TransitionEventTopic { get; set; } = "state-transitions";
    public int ConcurrencyRetryAttempts { get; set; } = 3;
    public string? ConnectionString { get; set; }
}

public enum StateMachineProvider
{
    Stateless,
    Mock
}


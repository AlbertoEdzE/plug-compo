namespace KSquare.EventBus.Consumers;

public sealed record ConsumerRegistration(Type MessageType, Type ConsumerType, string Topic, string Subscription);

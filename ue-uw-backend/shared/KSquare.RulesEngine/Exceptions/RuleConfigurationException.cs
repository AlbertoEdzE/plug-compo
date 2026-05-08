namespace KSquare.RulesEngine.Exceptions;

public sealed class RuleConfigurationException(string message, Exception? inner = null) : Exception(message, inner);


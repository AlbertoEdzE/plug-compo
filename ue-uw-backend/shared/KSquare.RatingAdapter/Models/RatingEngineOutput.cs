namespace KSquare.RatingAdapter.Models;

public record RatingEngineOutput(IDictionary<string, object?> Response, int HttpStatusCode);


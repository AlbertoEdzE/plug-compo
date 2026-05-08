namespace KSquare.FormTemplates.Contracts;

public interface IFormFieldMapper
{
    IDictionary<string, string?> MapFields<TSource>(string templateName, TSource source) where TSource : class;
}


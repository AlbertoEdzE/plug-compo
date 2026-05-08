using KSquare.DocumentExtraction.Models;
using KSquare.ExtractionMapper.Models;

namespace KSquare.ExtractionMapper.Contracts;

public interface IExtractionMapper
{
    MappingResult<T> Map<T>(ExtractionResult extraction, string documentType) where T : class, new();
    MappingResult<IDictionary<string, object?>> MapToDictionary(ExtractionResult extraction, string documentType);
}


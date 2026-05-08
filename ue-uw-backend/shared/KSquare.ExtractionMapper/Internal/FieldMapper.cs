using System.Reflection;
using KSquare.DocumentExtraction.Models;
using KSquare.ExtractionMapper.Configuration;
using KSquare.ExtractionMapper.Contracts;
using KSquare.ExtractionMapper.Models;

namespace KSquare.ExtractionMapper.Internal;

internal sealed class FieldMapper(ExtractionMapperOptions options, IMappingRuleProvider ruleProvider) : IExtractionMapper
{
    public MappingResult<T> Map<T>(ExtractionResult extraction, string documentType) where T : class, new()
    {
        var rules = ruleProvider.GetRulesAsync(documentType).ConfigureAwait(false).GetAwaiter().GetResult();
        var warnings = new List<MappingWarning>();
        var mappedFields = new List<MappedField>(rules.Rules.Count);
        var fieldLookup = CreateFieldLookup(extraction);

        var target = new T();

        foreach (var rule in rules.Rules)
        {
            ApplyRuleToTarget(rule, fieldLookup, target, mappedFields, warnings);
        }

        if (options.StrictMode)
        {
            var missingRequired = warnings.Where(w => w.Severity == WarningSeverity.RequiredFieldMissing).ToArray();
            if (missingRequired.Length > 0)
            {
                var fields = string.Join(", ", missingRequired.Select(w => w.FieldName));
                throw new InvalidOperationException($"Required fields missing: {fields}");
            }
        }

        return new MappingResult<T>
        {
            Value = target,
            MappedFields = mappedFields,
            Warnings = warnings
        };
    }

    public MappingResult<IDictionary<string, object?>> MapToDictionary(ExtractionResult extraction, string documentType)
    {
        var rules = ruleProvider.GetRulesAsync(documentType).ConfigureAwait(false).GetAwaiter().GetResult();
        var warnings = new List<MappingWarning>();
        var mappedFields = new List<MappedField>(rules.Rules.Count);
        var fieldLookup = CreateFieldLookup(extraction);

        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in rules.Rules)
        {
            var (typedValue, sourceFieldName, sourceConfidence) = EvaluateRule(rule, fieldLookup, warnings);
            dict[rule.CanonicalField] = typedValue;

            mappedFields.Add(new MappedField
            {
                CanonicalFieldName = rule.CanonicalField,
                SourceFieldName = sourceFieldName,
                Value = typedValue,
                SourceConfidence = sourceConfidence,
                RuleApplied = rule.RuleId
            });
        }

        if (options.StrictMode)
        {
            var missingRequired = warnings.Where(w => w.Severity == WarningSeverity.RequiredFieldMissing).ToArray();
            if (missingRequired.Length > 0)
            {
                var fields = string.Join(", ", missingRequired.Select(w => w.FieldName));
                throw new InvalidOperationException($"Required fields missing: {fields}");
            }
        }

        return new MappingResult<IDictionary<string, object?>>
        {
            Value = dict,
            MappedFields = mappedFields,
            Warnings = warnings
        };
    }

    private static Dictionary<string, ExtractedField> CreateFieldLookup(ExtractionResult extraction)
    {
        var lookup = new Dictionary<string, ExtractedField>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in extraction.Fields)
        {
            if (!lookup.TryGetValue(field.Name, out var existing))
            {
                lookup[field.Name] = field;
                continue;
            }

            if (field.Confidence > existing.Confidence)
            {
                lookup[field.Name] = field;
            }
        }

        return lookup;
    }

    private void ApplyRuleToTarget<T>(
        FieldMappingRule rule,
        IReadOnlyDictionary<string, ExtractedField> fieldLookup,
        T target,
        ICollection<MappedField> mappedFields,
        ICollection<MappingWarning> warnings)
        where T : class
    {
        var (typedValue, sourceFieldName, sourceConfidence) = EvaluateRule(rule, fieldLookup, warnings);

        var property = typeof(T)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(p => p.CanWrite && p.Name.Equals(rule.CanonicalField, StringComparison.OrdinalIgnoreCase));

        if (property is null)
        {
            warnings.Add(new MappingWarning(
                rule.CanonicalField,
                $"Target property '{rule.CanonicalField}' not found on {typeof(T).Name}.",
                WarningSeverity.Info));
        }
        else
        {
            if (!TryCoerceToPropertyType(typedValue, property.PropertyType, out var coerced))
            {
                warnings.Add(new MappingWarning(
                    rule.CanonicalField,
                    $"Could not assign value to property '{rule.CanonicalField}'.",
                    WarningSeverity.ParseFailure));
            }
            else
            {
                property.SetValue(target, coerced);
            }
        }

        mappedFields.Add(new MappedField
        {
            CanonicalFieldName = rule.CanonicalField,
            SourceFieldName = sourceFieldName,
            Value = typedValue,
            SourceConfidence = sourceConfidence,
            RuleApplied = rule.RuleId
        });
    }

    private static (object? Value, string? SourceFieldName, float SourceConfidence) EvaluateRule(
        FieldMappingRule rule,
        IReadOnlyDictionary<string, ExtractedField> fieldLookup,
        ICollection<MappingWarning> warnings)
    {
        ExtractedField? sourceField = null;
        foreach (var sourceName in rule.SourceFieldNames)
        {
            if (fieldLookup.TryGetValue(sourceName, out var field))
            {
                sourceField = field;
                break;
            }
        }

        var rawValue = sourceField?.Value;
        var sourceFieldName = sourceField?.Name;
        var sourceConfidence = sourceField?.Confidence ?? 1.0f;

        if (sourceField is null && rule.DefaultValue is not null)
        {
            rawValue = rule.DefaultValue;
            sourceFieldName = null;
            sourceConfidence = 1.0f;
        }

        if (rawValue is null)
        {
            if (rule.Required)
            {
                warnings.Add(new MappingWarning(
                    rule.CanonicalField,
                    $"Required field '{rule.CanonicalField}' was not found in extraction output.",
                    WarningSeverity.RequiredFieldMissing));
            }

            if (sourceField is not null && sourceField.Confidence < 0.75f)
            {
                warnings.Add(new MappingWarning(
                    rule.CanonicalField,
                    $"Field '{rule.CanonicalField}' has low source confidence ({sourceField.Confidence:0.00}).",
                    WarningSeverity.LowConfidence));
            }

            return (null, sourceFieldName, sourceConfidence);
        }

        if (!TransformEngine.TryApplyAndConvert(rawValue, rule.TransformExpression, rule.TargetType, out var typedValue, out var failureMessage))
        {
            warnings.Add(new MappingWarning(
                rule.CanonicalField,
                failureMessage ?? $"Could not map '{rule.CanonicalField}'.",
                WarningSeverity.ParseFailure));
            typedValue = null;
        }

        if (sourceField is not null && sourceField.Confidence < 0.75f)
        {
            warnings.Add(new MappingWarning(
                rule.CanonicalField,
                $"Field '{rule.CanonicalField}' has low source confidence ({sourceField.Confidence:0.00}).",
                WarningSeverity.LowConfidence));
        }

        return (typedValue, sourceFieldName, sourceConfidence);
    }

    private static bool TryCoerceToPropertyType(object? value, Type propertyType, out object? coerced)
    {
        coerced = null;

        if (value is null)
        {
            coerced = null;
            return IsNullable(propertyType);
        }

        var targetType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (targetType.IsInstanceOfType(value))
        {
            coerced = value;
            return true;
        }

        if (targetType == typeof(DateOnly))
        {
            if (value is DateTime dt)
            {
                coerced = DateOnly.FromDateTime(dt);
                return true;
            }

            if (value is string s && DateOnly.TryParse(s, out var d))
            {
                coerced = d;
                return true;
            }

            return false;
        }

        try
        {
            coerced = Convert.ChangeType(value, targetType);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsNullable(Type t) => !t.IsValueType || Nullable.GetUnderlyingType(t) is not null;
}


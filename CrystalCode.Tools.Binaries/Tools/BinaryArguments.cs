using System.Text.Json;

namespace CrystalCode.Tools.Binaries.Tools;

/// <summary>
/// Shared JSON argument helpers for the binary inspection tools.
/// </summary>
internal static class BinaryArguments
{
    public static bool TryOpen(
        string json,
        out JsonElement root,
        out JsonDocument? document,
        out string? error)
    {
        root = default;
        document = null;
        error = null;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            error = "Tool arguments are not valid JSON.";
            return false;
        }

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();
            document = null;
            error = "Tool arguments must be a JSON object.";
            return false;
        }

        root = document.RootElement;
        return true;
    }

    public static bool TryRejectUnknown(JsonElement root, string[] allowed, out string? error)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (!Array.Exists(allowed, name => string.Equals(name, property.Name, StringComparison.Ordinal)))
            {
                error = $"Tool arguments must contain only {JoinNames(allowed)}.";
                return false;
            }
        }

        error = null;
        return true;
    }

    public static bool TryReadRequiredString(
        JsonElement root,
        string name,
        out string value,
        out string? error)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
        {
            error = $"Argument '{name}' must be a non-empty string.";
            return false;
        }

        var text = element.GetString()!.Trim();
        if (text.Length == 0)
        {
            error = $"Argument '{name}' must be a non-empty string.";
            return false;
        }

        value = text;
        error = null;
        return true;
    }

    public static bool TryReadOptionalString(
        JsonElement root,
        string name,
        out string? value,
        out string? error)
    {
        value = null;
        error = null;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            error = $"Argument '{name}' must be a string.";
            return false;
        }

        var text = element.GetString()!.Trim();
        value = text.Length > 0 ? text : null;
        return true;
    }

    public static bool TryReadOptionalInteger(
        JsonElement root,
        string name,
        long minimum,
        long maximum,
        out long? value,
        out string? error)
    {
        value = null;
        error = null;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Number
            || !element.TryGetInt64(out var parsed)
            || parsed < minimum
            || parsed > maximum)
        {
            error = RangeError(name, minimum, maximum);
            return false;
        }

        value = parsed;
        return true;
    }

    public static bool TryReadOptionalChoice(
        JsonElement root,
        string name,
        string[] choices,
        out string? value,
        out string? error)
    {
        value = null;
        error = null;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var text = element.GetString()!.Trim();
            foreach (var choice in choices)
            {
                if (string.Equals(choice, text, StringComparison.OrdinalIgnoreCase))
                {
                    value = choice;
                    return true;
                }
            }
        }

        error = $"Argument '{name}' must be {JoinChoices(choices)}.";
        return false;
    }

    private static string RangeError(string name, long minimum, long maximum)
    {
        if (maximum != long.MaxValue)
        {
            return $"Argument '{name}' must be an integer between {minimum} and {maximum}.";
        }

        return minimum == 0
            ? $"Argument '{name}' must be a non-negative integer."
            : $"Argument '{name}' must be a positive integer.";
    }

    private static string JoinNames(string[] names) => names.Length switch
    {
        0 => string.Empty,
        1 => names[0],
        2 => $"{names[0]} and {names[1]}",
        _ => string.Join(", ", names[..^1]) + $", and {names[^1]}"
    };

    private static string JoinChoices(string[] choices) => choices.Length switch
    {
        0 => string.Empty,
        1 => $"'{choices[0]}'",
        2 => $"'{choices[0]}' or '{choices[1]}'",
        _ => string.Join(", ", choices[..^1].Select(choice => $"'{choice}'"))
            + $", or '{choices[^1]}'"
    };
}

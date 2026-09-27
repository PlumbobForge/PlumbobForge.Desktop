using System;
using System.IO;
using System.Linq;

namespace PlumbobForge.Desktop.Utils;

public static class NameValidator
{
    private static readonly char[] IllegalChars = Path.GetInvalidFileNameChars()
        .Union(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' })
        .Distinct()
        .ToArray();

    public static bool IsValidName(string? name, out string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            errorMessage = "Name cannot be empty.";
            return false;
        }

        var trimmed = name.Trim();

        var invalidInName = trimmed.Where(c => IllegalChars.Contains(c) || c < 32).Distinct().ToArray();
        if (invalidInName.Length > 0)
        {
            var charsStr = string.Join(" ", invalidInName.Select(c => $"'{c}'"));
            errorMessage = $"Name contains illegal characters: {charsStr}";
            return false;
        }

        if (trimmed.EndsWith('.') || trimmed.EndsWith(' '))
        {
            errorMessage = "Name cannot end with a period or space.";
            return false;
        }

        errorMessage = null;
        return true;
    }
}

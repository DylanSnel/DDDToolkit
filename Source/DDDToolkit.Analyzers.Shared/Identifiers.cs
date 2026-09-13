using System.Text;

namespace DDDToolkit.Analyzers.Common;

internal static class Identifiers
{
    /// <summary>Turns an assembly name into something usable as a namespace ("My-App.Core" → "My_App.Core").</summary>
    public static string NamespaceFrom(string? assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            return "DDDToolkitGenerated";
        }

        var builder = new StringBuilder(assemblyName!.Length);
        var startOfSegment = true;
        foreach (var character in assemblyName)
        {
            if (character == '.')
            {
                builder.Append('.');
                startOfSegment = true;
                continue;
            }

            var valid = char.IsLetter(character) || character == '_' || (!startOfSegment && char.IsDigit(character));
            builder.Append(valid ? character : '_');
            startOfSegment = false;
        }

        return builder.ToString();
    }
}

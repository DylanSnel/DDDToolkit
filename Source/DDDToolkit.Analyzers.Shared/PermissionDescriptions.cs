using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DDDToolkit.Analyzers.Common;

/// <summary>
/// What a permission of a <c>[Permissions]</c> class lets somebody do, as the screens that put permissions into roles
/// show it: the <c>Description</c> said on one of its levels, or else the constant's <c>&lt;summary&gt;</c>. Tenancy's
/// generator writes it into the catalogue, and the core reports a permission that has neither (DDD00099).
/// </summary>
internal static class PermissionDescriptions
{
    /// <summary>
    /// The <c>Description</c> the first of <paramref name="levels"/> that says one says, or null. A description of
    /// nothing but white space is none.
    /// </summary>
    public static string? Said(IEnumerable<AttributeData> levels)
    {
        foreach (var level in levels)
        {
            foreach (var named in level.NamedArguments)
            {
                if (named.Key == "Description" && named.Value.Value is string said && !string.IsNullOrWhiteSpace(said))
                {
                    return said;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The text of a constant's <c>&lt;summary&gt;</c>, read off the comment lines written above it, whether the project
    /// parses documentation comments or not: the compiler keeps the lines either way, as structured trivia where the
    /// project writes a documentation file and as plain comments where it does not. Tags dropped, a <c>cref</c> as the
    /// name it points at, white space collapsed. Null where there is none, or it says nothing.
    /// </summary>
    public static string? Summary(IFieldSymbol field, CancellationToken cancellationToken)
    {
        if (field.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken) is not { } declarator
            || declarator.FirstAncestorOrSelf<FieldDeclarationSyntax>() is not { } declaration)
        {
            return null;
        }

        var comment = new StringBuilder();
        foreach (var line in declaration.GetLeadingTrivia().ToFullString().Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("///", StringComparison.Ordinal))
            {
                comment.Append(trimmed.Substring(3)).Append(' ');
            }
        }

        var text = comment.ToString();
        var start = text.IndexOf("<summary>", StringComparison.Ordinal);
        var end = text.IndexOf("</summary>", StringComparison.Ordinal);
        if (start < 0 || end < start)
        {
            return null;
        }

        var summary = text.Substring(start + "<summary>".Length, end - start - "<summary>".Length);
        summary = Regex.Replace(summary, "<(see|seealso)\\s+(cref|langword|href)=\"(?:[A-Z]:)?([^\"]+)\"\\s*/>", static match => LastName(match.Groups[3].Value));
        summary = Regex.Replace(summary, "<(paramref|typeparamref)\\s+name=\"([^\"]+)\"\\s*/>", "$2");
        summary = Regex.Replace(summary, "<[^>]+>", string.Empty);
        summary = summary.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&apos;", "'").Replace("&amp;", "&");
        summary = Regex.Replace(summary, "\\s+", " ").Trim();
        return summary.Length == 0 ? null : summary;
    }

    /// <summary>Whether one of <paramref name="levels"/> retires the permission: known, and shown on no screen that gives it.</summary>
    public static bool Retired(IEnumerable<AttributeData> levels)
        => levels.Any(static level => level.NamedArguments.Any(static named => named.Key == "Retired" && named.Value.Value is true));

    private static string LastName(string reference)
    {
        var withoutArguments = reference.Split('(')[0];
        var dot = withoutArguments.LastIndexOf('.');
        return dot < 0 ? withoutArguments : withoutArguments.Substring(dot + 1);
    }
}

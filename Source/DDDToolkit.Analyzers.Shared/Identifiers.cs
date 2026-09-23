using System.Text;

namespace DDDToolkit.Analyzers.Common;

internal static class Identifiers
{
    /// <summary>
    /// The name of the id generated from <c>[AggregateRoot&lt;Guid&gt;]</c> or <c>[Entity&lt;Guid&gt;]</c>:
    /// the entity's own name with "Id" appended, so <c>Order</c> gets <c>OrderId</c>.
    /// <para>
    /// This is the only place the name is derived. The id type, its EF Core converter, its GraphQL
    /// binding and its registration entries are all produced from one definition that carries this
    /// name, so they cannot drift apart.
    /// </para>
    /// </summary>
    public static string IdNameFor(string entityName) => entityName + "Id";

    /// <summary>
    /// The prefix a generated id gets when the attribute names none: no prefix, exactly like
    /// <c>[EntityId&lt;Guid&gt;]</c> without one.
    /// <para>
    /// Inventing a prefix from the type name (<c>Order</c> → "ORD") would be a guess that changes the
    /// textual form of every id whenever the class is renamed, and there is no rule that abbreviates
    /// well for every name. A prefix is written into logs, URLs and support tickets, so it is a
    /// decision for the author: pass it as the first argument, <c>[AggregateRoot&lt;Guid&gt;("ORD")]</c>.
    /// </para>
    /// </summary>
    public const string DefaultIdPrefix = "";

    /// <summary>
    /// The parameter name for a property: <c>Amount</c> → <c>amount</c>, <c>IBAN</c> → <c>iban</c>,
    /// <c>URLPath</c> → <c>urlPath</c>, and <c>Class</c> → <c>@class</c> where the result is a keyword.
    /// </summary>
    public static string ParameterNameFor(string propertyName)
    {
        var upper = 0;
        while (upper < propertyName.Length && char.IsUpper(propertyName[upper]))
        {
            upper++;
        }

        var camel = upper switch
        {
            0 => propertyName,
            _ when upper == propertyName.Length => propertyName.ToLowerInvariant(),
            1 => char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1),
            _ => propertyName.Substring(0, upper - 1).ToLowerInvariant() + propertyName.Substring(upper - 1),
        };

        return Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetKeywordKind(camel) == Microsoft.CodeAnalysis.CSharp.SyntaxKind.None
            ? camel
            : "@" + camel;
    }

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

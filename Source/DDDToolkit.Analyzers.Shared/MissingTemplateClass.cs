using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace DDDToolkit.Analyzers.Common;

/// <summary>
/// What the code fix for DDD00044 and DDD00049 needs to declare the class a template names and nobody
/// declared: the template attribute, the names to look for the id under, and the class's name. The
/// generator works these out, because it already holds the symbols; the fix reads them off the diagnostic.
/// <para>
/// The class is named after the one the diagnostic is reported on: <c>ShopSeat</c>, declared with
/// <c>[SeatAggregate]</c>, has the prefix <c>Shop</c>, so the tenant it is missing is <c>ShopTenant</c>; <c>ShopUnit</c>,
/// declared with <c>[OrganizationUnit]</c>, ends in its last word and has the same prefix. With a prefix, the new
/// class ends in the last word of the missing template, the way <c>ShopUnit</c> does: an organization missing its
/// unit gets <c>ShopUnit</c>, not <c>ShopOrganizationUnit</c>. A class whose name ends in neither gives no prefix, and
/// neither does one named for its template alone, as <c>Seat</c> and <c>OrganizationUnit</c> are. The new class is
/// then called after the missing template in full, <c>Tenant</c> or <c>OrganizationUnit</c>, since its last word
/// alone, such as <c>Unit</c>, would be a name too general to declare.
/// </para>
/// <para>
/// The id is looked for under the parent's id type parameter without its <c>T</c>: <c>TTenantId</c> is looked
/// for as <c>TenantId</c>. A package may name that parameter shorter than the application names its id, as
/// <c>OrganizationUnitEntity&lt;TUnitId&gt;</c> does for an application's <c>OrganizationUnitId</c>, so when that name
/// finds nothing the fix looks for the template's own name with <c>Id</c> after it as well.
/// </para>
/// </summary>
internal static class MissingTemplateClass
{
    /// <summary>The diagnostic property holding the missing template attribute's metadata name.</summary>
    public const string TemplateProperty = "Template";

    /// <summary>The diagnostic property holding the name of the id type the new class is declared with, <c>TenantId</c>.</summary>
    public const string IdNameProperty = "IdName";

    /// <summary>
    /// The diagnostic property holding the name the id is looked for under when none is called
    /// <see cref="IdNameProperty"/>: the template's name with <c>Id</c> after it, <c>OrganizationUnitId</c>. Absent when
    /// the two names are the same.
    /// </summary>
    public const string FallbackIdNameProperty = "FallbackIdName";

    /// <summary>The diagnostic property holding the new class's name, <c>ShopTenant</c>.</summary>
    public const string ClassNameProperty = "ClassName";

    /// <summary>
    /// The properties for a diagnostic reported on <paramref name="reportedOn"/>, declared with
    /// <paramref name="reportedWith"/>, about the missing class declared with the attribute of
    /// <paramref name="missingMetadataName"/>. Empty when the attribute or its parent cannot be found, which
    /// leaves the diagnostic without a fix rather than with a wrong one.
    /// </summary>
    /// <param name="compilation">The compilation the diagnostic is reported in.</param>
    /// <param name="missingMetadataName">The metadata name of the template attribute nobody declared a class with.</param>
    /// <param name="reportedOn">The name of the class the diagnostic is reported on.</param>
    /// <param name="reportedWith">That class's template attribute, as the author writes it: <c>SeatAggregate</c>.</param>
    public static EquatableArray<string> Properties(Compilation compilation, string missingMetadataName, string reportedOn, string? reportedWith)
    {
        if (compilation.GetTypeByMetadataName(missingMetadataName) is not { } attribute
            || EntityDeclarations.TemplateOf(attribute) is not { Parent: { TypeParameters.Length: > 0 } parent })
        {
            return EquatableArray<string>.Empty;
        }

        var missing = ShortName(AttributeName(attribute.Name));
        var prefix = string.Empty;

        // A class called what its template is called has no prefix, whatever that name ends in: the
        // Organization of OrganizationUnit is the template's own first word, not the application's.
        if (reportedWith is not null && !string.Equals(reportedOn, ShortName(reportedWith), StringComparison.Ordinal))
        {
            foreach (var own in Endings(ShortName(reportedWith)))
            {
                if (reportedOn.Length > own.Length && reportedOn.EndsWith(own, StringComparison.Ordinal))
                {
                    prefix = reportedOn.Substring(0, reportedOn.Length - own.Length);
                    break;
                }
            }
        }

        var idName = IdName(parent.TypeParameters[0].Name);
        var properties = new List<string>
        {
            TemplateProperty, missingMetadataName,
            IdNameProperty, idName,
            ClassNameProperty, prefix.Length > 0 ? prefix + LastWord(missing) : missing,
        };

        if (!string.Equals(missing + "Id", idName, StringComparison.Ordinal))
        {
            properties.Add(FallbackIdNameProperty);
            properties.Add(missing + "Id");
        }

        return new EquatableArray<string>(properties.ToArray());
    }

    /// <summary>
    /// A template's short name, the noun a class declared with it ends in: <c>TenantAggregate</c> is
    /// <c>Tenant</c>, <c>InvoiceLineEntity</c> is <c>InvoiceLine</c>, and <c>OrganizationUnit</c> stays itself.
    /// </summary>
    public static string ShortName(string attributeName)
    {
        foreach (var suffix in new[] { "AggregateRoot", "Aggregate", "Entity" })
        {
            if (attributeName.Length > suffix.Length && attributeName.EndsWith(suffix, StringComparison.Ordinal))
            {
                return attributeName.Substring(0, attributeName.Length - suffix.Length);
            }
        }

        return attributeName;
    }

    /// <summary>
    /// The short name, then each shorter ending of it that starts a word: <c>OrganizationUnit</c>, then
    /// <c>Unit</c>. An application calls its unit <c>ShopUnit</c> rather than <c>ShopOrganizationUnit</c>.
    /// </summary>
    private static IEnumerable<string> Endings(string shortName)
    {
        yield return shortName;
        for (var index = 1; index < shortName.Length; index++)
        {
            if (char.IsUpper(shortName[index]))
            {
                yield return shortName.Substring(index);
            }
        }
    }

    /// <summary>The shortest ending of <see cref="Endings"/>: the last word, <c>Unit</c> of <c>OrganizationUnit</c>.</summary>
    private static string LastWord(string shortName)
    {
        var last = shortName;
        foreach (var ending in Endings(shortName))
        {
            last = ending;
        }

        return last;
    }

    /// <summary>The attribute as the author writes it: <c>TenantAggregateAttribute</c> is <c>TenantAggregate</c>.</summary>
    private static string AttributeName(string name)
        => name.Length > "Attribute".Length && name.EndsWith("Attribute", StringComparison.Ordinal)
            ? name.Substring(0, name.Length - "Attribute".Length)
            : name;

    /// <summary><c>TTenantId</c> is <c>TenantId</c>; a name that does not start with a <c>T</c> and a capital stays itself.</summary>
    private static string IdName(string typeParameter)
        => typeParameter.Length > 1 && typeParameter[0] == 'T' && char.IsUpper(typeParameter[1])
            ? typeParameter.Substring(1)
            : typeParameter;
}

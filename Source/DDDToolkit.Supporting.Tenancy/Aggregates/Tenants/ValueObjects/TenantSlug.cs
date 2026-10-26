using System.Text.RegularExpressions;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Validation;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// The short name a tenant is selected by, such as <c>harbor</c>: 2 to 63 characters, each a lowercase
/// letter, a digit or a dash, the first not a dash. It is set when the tenant is provisioned and never
/// changes, because it is what callers send to say which tenant they mean.
/// <para>
/// <see cref="Create"/> trims and lowercases what it is given, so <c>" Harbor "</c> and <c>harbor</c> are
/// the same slug. The generator adds <c>ValidTenantSlug</c>, the twin that is valid by construction.
/// </para>
/// </summary>
[SingleValueObject<string>(ColumnLength: MaxLength)]
public partial record TenantSlug
{
    /// <summary>The longest slug: 63 characters, the limit of one DNS label.</summary>
    public const int MaxLength = 63;

    /// <summary>
    /// What a slug looks like. A slug may end in a dash, which a DNS label may not: a host that puts slugs
    /// in host names checks that itself.
    /// </summary>
    public const string Pattern = "^[a-z0-9][a-z0-9-]{1,62}$";

    /// <summary>A slug from what a person typed: trimmed and lowercased, and not yet checked.</summary>
    /// <param name="value">The slug as given.</param>
    public static TenantSlug Create(string value) => new((value ?? string.Empty).Trim().ToLowerInvariant());

    /// <inheritdoc />
    protected override void Validate(ValidationErrorBuilder errors)
    {
        if (!SlugPattern().IsMatch(Value ?? string.Empty))
        {
            // The property of a value with one member says nothing to a form; the argument names the input.
            errors.Add(
                TenancyRefusals.TemplateOf(TenancyRefusals.InvalidSlug),
                nameof(Value),
                TenancyRefusals.InvalidSlug,
                Value,
                TenancyRefusals.ArgumentsOf(TenancyRefusals.InvalidSlug));
        }
    }

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
}

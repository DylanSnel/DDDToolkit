using System;
using System.Globalization;
using System.Text;

namespace DDDToolkit.BaseTypes;

/// <summary>
/// The name an event gets when nobody names it: its module in kebab case, a dot, and its class name in
/// kebab case. <c>OrderPlaced</c> in <c>[assembly: Module("Ordering")]</c> is <c>ordering.order-placed</c>.
/// A class name that ends in <c>V</c> and a number carries its version there: <c>OrderPlacedV2</c> is
/// <c>ordering.order-placed</c> at version 2, so every version of one event shares one name.
/// <para>
/// This file is compiled twice, into the runtime and into every analyzer assembly, because the name the
/// generated registration writes out and the name <c>DomainEventName.Of</c> reads by reflection have
/// to be the same name. One source for both is what makes that true by construction instead of by care.
/// It targets what both compilers accept: no APIs newer than netstandard2.0.
/// </para>
/// </summary>
internal static class EventNameConvention
{
    /// <summary>
    /// Splits a class name into the name it stands for and the version its suffix gives, if any.
    /// <c>OrderPlacedV2</c> is <c>("OrderPlaced", 2)</c>, <c>OrderPlaced</c> is <c>("OrderPlaced", null)</c>.
    /// </summary>
    /// <remarks>
    /// A suffix that cannot be a version, <c>V0</c> or <c>V01</c>, is <see cref="Suffix.Malformed"/>: the
    /// analyzer refuses it, and anything compiled without the analyzer reads the whole class name as the
    /// name. Digits that do not follow a <c>V</c>, as in <c>Level2Reached</c>, are just part of the name.
    /// </remarks>
    public static Suffix Split(string typeName)
    {
        var digits = typeName.Length;
        while (digits > 0 && typeName[digits - 1] >= '0' && typeName[digits - 1] <= '9')
        {
            digits--;
        }

        // Nothing after the V, no V before the digits, or nothing before the V: not a version suffix.
        if (digits == typeName.Length || digits < 2 || typeName[digits - 1] != 'V')
        {
            return new Suffix(typeName, null, Malformed: false);
        }

        var number = typeName.Substring(digits);
        if (number[0] == '0' || !int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var version))
        {
            return new Suffix(typeName, null, Malformed: true);
        }

        return new Suffix(typeName.Substring(0, digits - 1), version, Malformed: false);
    }

    /// <summary>The conventional name of an event class: <c>{module}.{name}</c>, or just the name outside a module.</summary>
    /// <param name="typeName">The class name, as <c>Type.Name</c> or the symbol's metadata name spells it.</param>
    /// <param name="module">The name in the assembly's <c>[assembly: Module]</c>, or null when it declares none.</param>
    public static string NameFor(string typeName, string? module)
    {
        var name = Kebab(Split(typeName).Name);
        var prefix = module is null ? string.Empty : Kebab(module);

        return prefix.Length == 0 ? name : prefix + "." + name;
    }

    /// <summary>
    /// The kebab case of a PascalCase identifier: <c>OrderPlaced</c> is <c>order-placed</c>,
    /// <c>HTTPRequestSent</c> is <c>http-request-sent</c>, <c>Ipv6Changed</c> is <c>ipv6-changed</c>. Anything
    /// that is not a letter or a digit becomes a single hyphen.
    /// </summary>
    public static string Kebab(string value)
    {
        var builder = new StringBuilder(value.Length + 8);

        for (var i = 0; i < value.Length; i++)
        {
            var character = value[i];

            if (!char.IsLetterOrDigit(character))
            {
                AppendHyphen(builder);
                continue;
            }

            if (char.IsUpper(character) && i > 0)
            {
                var previous = value[i - 1];
                var next = i + 1 < value.Length ? value[i + 1] : '\0';

                // A word starts at an upper case letter after a lower case one or a digit ("orderPlaced",
                // "v6Changed"), and at the last capital of an acronym that runs into a word ("HTTPRequest").
                if (char.IsLower(previous) || char.IsDigit(previous) || (char.IsUpper(previous) && char.IsLower(next)))
                {
                    AppendHyphen(builder);
                }
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        if (builder.Length > 0 && builder[builder.Length - 1] == '-')
        {
            builder.Length--;
        }

        return builder.ToString();
    }

    private static void AppendHyphen(StringBuilder builder)
    {
        if (builder.Length > 0 && builder[builder.Length - 1] != '-')
        {
            builder.Append('-');
        }
    }

    /// <summary>What a class name says about itself.</summary>
    /// <param name="Name">The class name without its version suffix.</param>
    /// <param name="Version">The version the suffix gives, or null when there is none.</param>
    /// <param name="Malformed">True for a suffix that looks like a version but cannot be one, <c>V0</c> or <c>V01</c>.</param>
    public readonly record struct Suffix(string Name, int? Version, bool Malformed);
}

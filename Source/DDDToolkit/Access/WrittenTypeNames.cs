namespace DDDToolkit.Access;

/// <summary>Types named as their authors write them, for the messages of the access checks and of what they keep.</summary>
internal static class WrittenTypeNames
{
    /// <summary>
    /// A type as its author writes it, for a message a developer reads: a case nested in its requirement is
    /// <c>BillingAccess.OnInvoice</c>, a case of a requirement closed over an id is
    /// <c>LedgerAccess&lt;LedgerId&gt;.On</c>, each type argument beside the type that declares it, and a generic
    /// type is <c>MemberHold&lt;ProjectId&gt;</c> rather than the runtime's <c>MemberHold`1</c>.
    /// </summary>
    /// <param name="type">The type to name.</param>
    public static string Of(Type type)
    {
        // A nested type carries the type arguments of the types around it in front of its own, and those
        // types are only had as their definitions: each is given the ones it declares itself.
        var arguments = type.IsConstructedGenericType ? type.GenericTypeArguments : [];
        var nesting = new List<Type>();
        for (var each = type; each is not null; each = each.DeclaringType)
        {
            nesting.Insert(0, each);
        }

        var name = string.Empty;
        var named = 0;
        foreach (var each in nesting)
        {
            name += (name.Length > 0 ? "." : string.Empty)
                    + (each.Name.IndexOf('`') is var arity and >= 0 ? each.Name[..arity] : each.Name);

            var upTo = Math.Min(each.GetGenericArguments().Length, arguments.Length);
            if (upTo > named)
            {
                name += "<" + string.Join(", ", arguments[named..upTo].Select(Of)) + ">";
                named = upTo;
            }
        }

        return name;
    }
}

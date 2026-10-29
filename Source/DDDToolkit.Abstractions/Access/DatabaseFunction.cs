namespace DDDToolkit.Abstractions.Access;

/// <summary>
/// A function of the database, named by its owner and its name, whatever schema it lives in: its logical name,
/// <c>owner/name</c>. A package ships the functions it writes as values of this, so an application names one by
/// a member it can find, and never by a text it types:
/// <code>
/// Roles.External(TenancyFunctions.RolesWithPermission)
/// </code>
/// Whatever writes the policies finds the function by its logical name, in the script of the context that defines
/// it or of another one it is written with, and writes it with the schema that context gives it.
/// </summary>
public sealed record DatabaseFunction
{
    /// <summary>A function of the database.</summary>
    /// <param name="owner">
    /// Its owner: the package or the module that defines it, in lower case letters, digits and dashes, such as
    /// <c>tenancy</c>; what a module's name is written as in a migration's file name.
    /// </param>
    /// <param name="name">Its name, in letters, digits and underscores, not starting with a digit: <c>current_seat</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="owner"/> or <paramref name="name"/> is not one of those.</exception>
    public DatabaseFunction(string owner, string name)
    {
        if (!IsOwner(owner))
        {
            throw new ArgumentException(
                "'" + owner + "' is no owner of a function. An owner is lower case letters, digits and dashes, neither starting nor ending with a dash, such as 'tenancy'.",
                nameof(owner));
        }

        if (!IsName(name))
        {
            throw new ArgumentException(
                "'" + name + "' is no name of a function. A name is letters, digits and underscores, not starting with a digit, such as 'current_seat'.",
                nameof(name));
        }

        Owner = owner;
        Name = name;
    }

    /// <summary>The function's owner: the package or the module that defines it.</summary>
    public string Owner { get; }

    /// <summary>The function's name, without a schema.</summary>
    public string Name { get; }

    /// <summary>The logical name, <c>owner/name</c>: what a script finds the function by.</summary>
    public string LogicalName => Owner + "/" + Name;

    /// <summary>The logical name.</summary>
    public override string ToString() => LogicalName;

    private static bool IsOwner(string? owner)
        => owner is { Length: > 0 }
           && owner[0] != '-'
           && owner[owner.Length - 1] != '-'
           && owner.All(character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');

    private static bool IsName(string? name)
        => name is { Length: > 0 }
           && !char.IsDigit(name[0])
           && name.All(character => character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_');
}

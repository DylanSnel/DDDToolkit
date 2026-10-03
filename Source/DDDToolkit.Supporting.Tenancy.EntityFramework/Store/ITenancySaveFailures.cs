using DDDToolkit.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>
/// Turns a failed save of Tenancy's store into the refusal it stands for. Some rules are kept by the database
/// where it sees more than the use case does; when such a rule fails the save, the caller should get the same
/// coded refusal the use case gives, not the database's error.
/// <para>
/// The unique indexes need none: each says in the mapping which refusal a save that breaks it gets, and the
/// toolkit's own interceptor makes it, on any database, before a translator is asked. A translator is for a
/// rule no index states, such as one a trigger of one database checks when the transaction commits.
/// </para>
/// <para>
/// A package for one database registers one, as a singleton or scoped; the store asks every registered one in
/// the order they were registered, and the first refusal wins. When none knows the failure, the exception is
/// thrown on as it was. A save that was refused already is not offered.
/// </para>
/// </summary>
public interface ITenancySaveFailures
{
    /// <summary>
    /// A refusal for <paramref name="failure"/>, the exception the save of <paramref name="context"/> failed with
    /// (a constraint a trigger checks at commit), or <see langword="null"/> when it is not one this knows.
    /// </summary>
    /// <param name="failure">What the save threw.</param>
    /// <param name="context">The context that saved: Tenancy's.</param>
    RefusalException? Translate(Exception failure, DbContext context);
}

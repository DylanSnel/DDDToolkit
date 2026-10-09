using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// Marks the rules of one kind of resource, the ones it is registered with, as those of the members of
/// <typeparamref name="TMember"/>, so the program that exports the database's policies writes the resource's
/// functions from the same rules:
/// <code>
/// public static class DocumentMembership
/// {
///     [MembershipRules&lt;DocumentShare&gt;]
///     public static MembershipRules Rules { get; } = new("documents", keys: [...], roles: [...]);
/// }
///
/// // Where the resource is registered
/// services.AddDocumentMembership&lt;DocumentsContext&gt;(DocumentMembership.Rules);
/// </code>
/// <para>
/// Membership on Postgres writes, for every kind of resource, the four set functions that answer its membership
/// questions in the database, and the lock that holds its members' rows to the keys its rules name. The export
/// runs before the application starts, and the rules the registration is called with are a value the build
/// cannot read, so the application marks them: the export writes the Membership package's contribution once for
/// every member so marked, closed over the member class the mark names. Two kinds of resource are two marks.
/// </para>
/// <para>
/// The member is a static property or field of type <see cref="MembershipRules"/>, readable from the project that
/// runs the export, which references the project that declares it: in a library it is public, in public types, which
/// that library's own build checks (DDD00070). One member is marked for each member class:
/// the export reports two, and one of another type (DDD00071); an application that references Membership on
/// Postgres and marks none is told that nothing of it is written (DDD00054).
/// </para>
/// </summary>
/// <typeparam name="TMember">The member class of the resource, declared with <c>[Member&lt;...&gt;]</c>.</typeparam>
[ApplicationMark]
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class MembershipRulesAttribute<TMember> : Attribute
    where TMember : class;

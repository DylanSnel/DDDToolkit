using System.Runtime.CompilerServices;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.HotChocolate.Attributes;
using HotChocolate;
using HotChocolate.Language;
using HotChocolate.Types;

namespace Library.Api;

// A library's GraphQL in two schemas. A member asks for their own loans; the library's staff ask for anybody's,
// extend a loan, and follow the loans that are overdue. The staff's fields are in classes marked [GraphQLSchema],
// every other class is in every schema.

/// <summary>The names the library's schemas are registered under, which its classes and its host agree on.</summary>
public static class LibrarySchemas
{
    /// <summary>What every member is offered.</summary>
    public const string Members = "members";

    /// <summary>What the library's staff are offered besides.</summary>
    public const string Staff = "staff";

    /// <summary>What the help desk is offered: one field of the staff's, and nothing else of theirs.</summary>
    public const string HelpDesk = "help-desk";
}

/// <summary>Who borrows: an id the schema shows as a <c>UUID</c>, bound by the toolkit's generated bindings.</summary>
[EntityId<Guid>("MBR")]
public readonly partial record struct MemberId;

/// <summary>A loan, as the library's application layer answers it.</summary>
/// <param name="Member">Who borrowed it.</param>
/// <param name="Title">What was borrowed.</param>
/// <param name="DueInDays">How many days are left.</param>
public sealed record LoanOverview(MemberId Member, string Title, int DueInDays);

/// <summary>The library's loans, in memory: what the fields read and change.</summary>
public sealed class LibraryDesk
{
    /// <summary>The member who asks, in these tests.</summary>
    public static readonly MemberId Ada = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));

    /// <summary>Another member, whose loans only the staff read.</summary>
    public static readonly MemberId Ben = new(Guid.Parse("22222222-2222-2222-2222-222222222222"));

    private readonly List<LoanOverview> _loans =
    [
        new(Ada, "Middlemarch", 12),
        new(Ben, "Bleak House", -3),
        new(Ben, "Persuasion", 5),
    ];

    /// <summary>The loans of <paramref name="member"/>.</summary>
    public IReadOnlyList<LoanOverview> LoansOf(MemberId member) => [.. _loans.Where(loan => loan.Member == member)];

    /// <summary>How many loans are overdue, of every member.</summary>
    public int Overdue => _loans.Count(loan => loan.DueInDays < 0);

    /// <summary>The loans that are overdue, of every member.</summary>
    public IReadOnlyList<LoanOverview> OverdueLoans => [.. _loans.Where(loan => loan.DueInDays < 0)];

    /// <summary>Gives <paramref name="member"/> more days for <paramref name="title"/>, and answers the loan as it is now.</summary>
    public LoanOverview Extend(MemberId member, string title, int days)
    {
        var index = _loans.FindIndex(loan => loan.Member == member && loan.Title == title);
        _loans[index] = _loans[index] with { DueInDays = _loans[index].DueInDays + days };
        return _loans[index];
    }
}

/// <summary>A loan as every schema shows it: HotChocolate's generator registers the class, in every schema.</summary>
[ObjectType<LoanOverview>]
public static partial class LoanType
{
    static partial void Configure(IObjectTypeDescriptor<LoanOverview> descriptor) => descriptor.Name("Loan");

    /// <summary>Whether the loan is overdue: a field the record does not have.</summary>
    public static bool GetOverdue([Parent] LoanOverview loan) => loan.DueInDays < 0;
}

/// <summary>What a member asks: their own loans. No mark, so it is in every schema.</summary>
public static class LoansQueries
{
    /// <summary>The asking member's loans.</summary>
    [Query]
    public static IReadOnlyList<LoanOverview> GetLoansOfMine([Service] LibraryDesk desk) => desk.LoansOf(LibraryDesk.Ada);
}

/// <summary>What the staff ask: anybody's loans. Only the staff's schema has it.</summary>
[GraphQLSchema(LibrarySchemas.Staff, OperationType.Query)]
public static class LoansStaffQueries
{
    /// <summary>The loans of any member.</summary>
    public static IReadOnlyList<LoanOverview> GetLoansOf(MemberId member, [Service] LibraryDesk desk) => desk.LoansOf(member);

    /// <summary>Not a field: marked so.</summary>
    [GraphQLIgnore]
    public static int GetSecretCount() => 42;
}

/// <summary>What the staff change: a loan's due date. Only the staff's schema has a mutation type at all.</summary>
[GraphQLSchema(LibrarySchemas.Staff, OperationType.Mutation)]
public static class LoansStaffMutations
{
    /// <summary>Gives a member more days for a loan.</summary>
    public static LoanOverview LoanExtend(MemberId member, string title, int days, [Service] LibraryDesk desk) => desk.Extend(member, title, days);
}

/// <summary>What the staff follow: each loan that is overdue. Only the staff's schema has a subscription type at all.</summary>
[GraphQLSchema(LibrarySchemas.Staff, OperationType.Subscription)]
public static class LoansStaffSubscriptions
{
    /// <summary>The stream the field below subscribes to, the overdue loans one by one: no field of its own.</summary>
    public static async IAsyncEnumerable<LoanOverview> SubscribeToOverdueLoans([Service] LibraryDesk desk, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var loan in desk.OverdueLoans)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return loan;
        }
    }

    /// <summary>A loan that is overdue.</summary>
    [Subscribe(With = nameof(SubscribeToOverdueLoans))]
    public static LoanOverview OnLoanOverdue([EventMessage] LoanOverview loan) => loan;
}

/// <summary>One field the staff and the help desk share, and the members' schema has not.</summary>
[GraphQLSchema(LibrarySchemas.Staff, OperationType.Query)]
[GraphQLSchema(LibrarySchemas.HelpDesk, OperationType.Query)]
public static class OverdueQueries
{
    /// <summary>How many loans are overdue.</summary>
    public static int GetOverdueCount([Service] LibraryDesk desk) => desk.Overdue;
}

using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.EntityFramework.Tests.Converters;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

// A help desk, to put row access rules against a real Postgres: tickets that belong to somebody, to a
// team, or to everyone, the comments on them, which are the ticket's own entities, and the reactions to a
// comment, which are the comment's.

[EntityId<Guid>]
public readonly partial record struct TicketId;

[EntityId<Guid>]
public readonly partial record struct TicketCommentId;

[EntityId<Guid>]
public readonly partial record struct TicketWatcherId;

[EntityId<Guid>]
public readonly partial record struct CommentReactionId;

public enum TicketStatus
{
    Open,
    Closed,
}

[AggregateRoot<TicketId>]
public partial class Ticket
{
    public Ticket(TicketId id, string title, Guid? owner, string? team, TicketStatus status, bool isPublic) : base(id)
    {
        Title = title;
        Owner = owner;
        Team = team;
        Status = status;
        IsPublic = isPublic;
    }

    public string Title { get; private set; }

    public Guid? Owner { get; private set; }

    public string? Team { get; private set; }

    public TicketStatus Status { get; private set; }

    public bool IsPublic { get; private set; }

    public partial IReadOnlyList<TicketComment> Comments { get; }

    public void Comment(string text) => _comments.Add(new TicketComment(TicketCommentId.CreateSequential(), text));

    /// <summary>The people who follow the ticket without owning it.</summary>
    public partial IReadOnlyList<TicketWatcher> Watchers { get; }

    public void Watch(Guid user) => _watchers.Add(new TicketWatcher(TicketWatcherId.CreateSequential(), user));
}

[Entity<TicketCommentId>]
public partial class TicketComment
{
    public TicketComment(TicketCommentId id, string text) : base(id) => Text = text;

    public string Text { get; private set; }

    /// <summary>Who reacted to the comment: entities of an entity, two tables away from the ticket.</summary>
    public partial IReadOnlyList<CommentReaction> Reactions { get; }

    public void React(Guid user) => _reactions.Add(new CommentReaction(CommentReactionId.CreateSequential(), user));
}

[Entity<CommentReactionId>]
public partial class CommentReaction
{
    public CommentReaction(CommentReactionId id, Guid user) : base(id) => User = user;

    public Guid User { get; private set; }
}

[Entity<TicketWatcherId>]
public partial class TicketWatcher
{
    public TicketWatcher(TicketWatcherId id, Guid user) : base(id) => User = user;

    public Guid User { get; private set; }
}

/// <summary>
/// Whether the caller watches the ticket: a question about the ticket's entities, which a policy on the
/// tickets table cannot ask itself, so it is a function the rules call.
/// </summary>
[AccessFunction<Ticket>("desk.is_watcher")]
public static partial class TicketWatchers
{
    public static bool Allows(Ticket ticket, Caller caller) => ticket.Watchers.Any(watcher => watcher.User == caller.UserId);
}

/// <summary>A watcher reads the tickets they watch.</summary>
[RowAccess<Ticket>(RowOperations.Read)]
public static partial class WatchersReadTheirTickets
{
    public static bool Allows(Ticket ticket, Caller caller) => TicketWatchers.Allows(ticket, caller);
}

/// <summary>
/// The tickets the caller watches, as the set of their ids: asked once per statement, where
/// <see cref="TicketWatchers"/> is asked once per row. Named relative to the desk, so the function lives in
/// the schema of whichever context maps the tickets.
/// </summary>
[AccessFunction<Ticket>("tickets_i_watch", Shape = AccessFunctionShape.Set)]
[AccessFunctions(Owner = "desk")]
public static partial class TicketsIWatch
{
    public static bool Allows(Ticket ticket, Caller caller) => ticket.Watchers.Any(watcher => watcher.User == caller.UserId);
}

/// <summary>The tickets of a team, only the open ones when asked: a set-shaped function that takes more than the caller.</summary>
[AccessFunction<Ticket>("desk.tickets_for_team", Shape = AccessFunctionShape.Set)]
public static partial class TicketsForTeam
{
    public static bool Allows(Ticket ticket, Caller caller, string team, bool openOnly)
        => ticket.Team == team && (!openOnly || ticket.Status == TicketStatus.Open);
}

/// <summary>
/// The tickets with a comment the caller reacted to, as the set of their ids: a question about the entities of
/// an entity, the reactions to a ticket's comments, two tables away from the ticket.
/// </summary>
[AccessFunction<Ticket>("desk.tickets_i_reacted_in", Shape = AccessFunctionShape.Set)]
public static partial class TicketsIReactedIn
{
    public static bool Allows(Ticket ticket, Caller caller)
        => ticket.Comments.Any(comment => comment.Reactions.Any(reaction => reaction.User == caller.UserId));
}

/// <summary>
/// Whether the ticket is public, or has a comment with the text asked for that the caller reacted to: the same
/// question about one row, with a parameter read between the comment and its reactions.
/// </summary>
[AccessFunction<Ticket>("desk.reacted_to_comment")]
public static partial class ReactedToComment
{
    public static bool Allows(Ticket ticket, Caller caller, string text)
        => ticket.IsPublic || ticket.Comments.Any(comment => comment.Text == text && comment.Reactions.Any(reaction => reaction.User == caller.UserId));
}

/// <summary>A watcher reads the tickets they watch, asked as a set, once per statement.</summary>
[RowAccess<Ticket>(RowOperations.Read)]
public static partial class WatchersReadTheTicketsTheyWatch
{
    public static bool Allows(Ticket ticket, Caller caller) => TicketsIWatch.Ids().Contains(ticket.Id);
}

/// <summary>What the desk knows of a caller, which a function it creates itself answers.</summary>
[AccessFunctions]
public static partial class DeskQuestions
{
    /// <summary>The caller's seat at the desk, or null for a caller who has none.</summary>
    [AccessScalar("desk.caller_seat")]
    public static partial Guid CallerSeat();

    /// <summary>Whether the caller is on duty at the desk, or null for a caller the desk does not know.</summary>
    [AccessScalar("desk.caller_on_duty")]
    public static partial bool CallerOnDuty();
}

/// <summary>Whoever is on duty does anything with every ticket: a rule that is one question and nothing else.</summary>
[RowAccess<Ticket>(RowOperations.All)]
public static partial class OnDutyHandleEveryTicket
{
    public static bool Allows(Ticket ticket, Caller caller) => DeskQuestions.CallerOnDuty();
}

/// <summary>A ticket is read from the seat that owns it.</summary>
[RowAccess<Ticket>(RowOperations.Read)]
public static partial class TicketsOfTheCallersSeat
{
    public static bool Allows(Ticket ticket, Caller caller) => ticket.Owner == DeskQuestions.CallerSeat();
}

/// <summary>A ticket is read from every seat but the one that owns it.</summary>
[RowAccess<Ticket>(RowOperations.Read)]
public static partial class TicketsOfAnotherSeat
{
    public static bool Allows(Ticket ticket, Caller caller) => ticket.Owner != DeskQuestions.CallerSeat();
}

/// <summary>A ticket is read from every seat but the one that owns it, written as not owning it.</summary>
[RowAccess<Ticket>(RowOperations.Read)]
public static partial class TicketsNotOfTheCallersSeat
{
    public static bool Allows(Ticket ticket, Caller caller) => !(ticket.Owner == DeskQuestions.CallerSeat());
}

/// <summary>An owner does anything with their own tickets.</summary>
[RowAccess<Ticket>(RowOperations.All)]
public static partial class OwnersHaveTheirTickets
{
    public static bool Allows(Ticket ticket, Caller caller) => caller.IsSignedIn && ticket.Owner == caller.UserId;
}

/// <summary>A teammate reads the team's tickets while they are open.</summary>
[RowAccess<Ticket>(RowOperations.Read)]
public static partial class TeammatesReadOpenTickets
{
    public static bool Allows(Ticket ticket, Caller caller)
        => ticket.Team != null && ticket.Team == caller.Claim("app_metadata.team") && ticket.Status == TicketStatus.Open;
}

/// <summary>
/// A teammate files a ticket for the team, owned by nobody yet: they may create it, and read it while it
/// is open, but not change it, so what they may add to it is only what they add while creating it.
/// </summary>
[RowAccess<Ticket>(RowOperations.Create)]
public static partial class TeammatesFileTicketsForTheTeam
{
    public static bool Allows(Ticket ticket, Caller caller)
        => ticket.Owner == null && ticket.Team != null && ticket.Team == caller.Claim("app_metadata.team");
}

/// <summary>Anybody reads a public ticket.</summary>
[RowAccess<Ticket>(RowOperations.Read)]
public static partial class PublicTicketsAreEveryones
{
    public static bool Allows(Ticket ticket, Caller caller) => ticket.IsPublic;
}

/// <summary>A teammate works on the team's tickets: reads and changes them, whichever column, as far as the rules for the row go.</summary>
[RowAccess<Ticket>(RowOperations.Read | RowOperations.Change, To = [RowAccessRoles.User])]
public static partial class TeammatesWorkOnTheTeamsTickets
{
    public static bool Allows(Ticket ticket, Caller caller) => ticket.Team != null && ticket.Team == caller.Claim("app_metadata.team");
}

/// <summary>Only its owner closes a ticket or opens it again: a column rule, one condition more for a change of its status.</summary>
[RowAccess<Ticket>(RowOperations.Change, Columns = [nameof(Ticket.Status)])]
public static partial class OwnersCloseTheirTickets
{
    public static bool Allows(Ticket ticket, Caller caller) => caller.IsSignedIn && ticket.Owner == caller.UserId;
}

/// <summary>A team's lead closes the team's tickets as well: a second column rule on the same column.</summary>
[RowAccess<Ticket>(RowOperations.Change, To = [RowAccessRoles.User], Columns = [nameof(Ticket.Status)])]
public static partial class LeadsCloseTheTeamsTickets
{
    public static bool Allows(Ticket ticket, Caller caller)
        => ticket.Team != null && ticket.Team == caller.Claim("app_metadata.team") && caller.Claim("app_metadata.lead") == "yes";
}

/// <summary>The desk's rules, each as the export takes it and as C# asks it.</summary>
public static class DeskRules
{
    public static readonly RowAccessRule Owners = RowAccessRule.For<Ticket>("Owners have their tickets", RowOperations.All, OwnersHaveTheirTickets.RowAccessSql);

    public static readonly RowAccessRule Teammates = RowAccessRule.For<Ticket>("Teammates read open tickets", RowOperations.Read, TeammatesReadOpenTickets.RowAccessSql);

    public static readonly RowAccessRule Public = RowAccessRule.For<Ticket>("Public tickets are everyones", RowOperations.Read, PublicTicketsAreEveryones.RowAccessSql);

    public static readonly RowAccessRule Watchers = RowAccessRule.For<Ticket>("Watchers read their tickets", RowOperations.Read, WatchersReadTheirTickets.RowAccessSql);

    public static readonly RowAccessRule TeamFiles = RowAccessRule.For<Ticket>("Teammates file tickets for the team", RowOperations.Create, TeammatesFileTicketsForTheTeam.RowAccessSql);

    public static readonly RowAccessFunction IsWatcher = RowAccessFunction.For<Ticket>("desk.is_watcher", TicketWatchers.RowAccessSql);

    public static readonly RowAccessFunction WatchedSet = RowAccessFunction.For<Ticket>(
        TicketsIWatch.Name, TicketsIWatch.RowAccessSql, TicketsIWatch.RowAccessOwner, TicketsIWatch.RowAccessParameters, TicketsIWatch.RowAccessShape);

    public static readonly RowAccessFunction ForTeam = RowAccessFunction.For<Ticket>(
        TicketsForTeam.Name, TicketsForTeam.RowAccessSql, TicketsForTeam.RowAccessOwner, TicketsForTeam.RowAccessParameters, TicketsForTeam.RowAccessShape);

    public static readonly RowAccessFunction ReactedSet = RowAccessFunction.For<Ticket>(
        TicketsIReactedIn.Name, TicketsIReactedIn.RowAccessSql, TicketsIReactedIn.RowAccessOwner, TicketsIReactedIn.RowAccessParameters, TicketsIReactedIn.RowAccessShape);

    public static readonly RowAccessFunction ReactedTo = RowAccessFunction.For<Ticket>(
        ReactedToComment.Name, ReactedToComment.RowAccessSql, ReactedToComment.RowAccessOwner, ReactedToComment.RowAccessParameters, ReactedToComment.RowAccessShape);

    public static readonly RowAccessRule WatchersBySet = RowAccessRule.For<Ticket>("Watchers read the tickets they watch", RowOperations.Read, WatchersReadTheTicketsTheyWatch.RowAccessSql);

    public static readonly RowAccessRule OfTheCallersSeat = RowAccessRule.For<Ticket>("Tickets of the callers seat", RowOperations.Read, TicketsOfTheCallersSeat.RowAccessSql);

    public static readonly RowAccessRule OfAnotherSeat = RowAccessRule.For<Ticket>("Tickets of another seat", RowOperations.Read, TicketsOfAnotherSeat.RowAccessSql);

    public static readonly RowAccessRule NotOfTheCallersSeat = RowAccessRule.For<Ticket>("Tickets not of the callers seat", RowOperations.Read, TicketsNotOfTheCallersSeat.RowAccessSql);

    public static readonly RowAccessRule OnDuty = RowAccessRule.For<Ticket>("On duty handle every ticket", RowOperations.All, OnDutyHandleEveryTicket.RowAccessSql);

    public static readonly RowAccessRule TeamWork = RowAccessRule.For<Ticket>("Teammates work on the teams tickets", RowOperations.Read | RowOperations.Change, TeammatesWorkOnTheTeamsTickets.RowAccessSql, RowAccessRoles.User);

    public static readonly RowAccessRule OwnersClose = RowAccessRule.ForColumns<Ticket>("Owners close their tickets", [nameof(Ticket.Status)], OwnersCloseTheirTickets.RowAccessSql);

    public static readonly RowAccessRule LeadsClose = RowAccessRule.ForColumns<Ticket>("Leads close the teams tickets", [nameof(Ticket.Status)], LeadsCloseTheTeamsTickets.RowAccessSql, RowAccessRoles.User);

    /// <summary>Whether the rules that let a caller read let <paramref name="caller"/> read <paramref name="ticket"/>, in C#.</summary>
    public static bool Reads(Ticket ticket, Caller caller, bool withPublic = true)
        => OwnersHaveTheirTickets.Allows(ticket, caller)
            || TeammatesReadOpenTickets.Allows(ticket, caller)
            || WatchersReadTheirTickets.Allows(ticket, caller)
            || (withPublic && PublicTicketsAreEveryones.Allows(ticket, caller));
}

public sealed class DeskContext(DbContextOptions<DeskContext> options) : DbContext(options)
{
    public DbSet<Ticket> Tickets => Set<Ticket>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("desk");

        // Entities of an entity: Entity Framework maps them once it is told whose they are.
        modelBuilder.Entity<Ticket>().OwnsMany(ticket => ticket.Comments, comment => comment.OwnsMany(each => each.Reactions));
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddEfTestsConverters();
    }

    /// <summary>A context on <paramref name="connectionString"/>, or on nothing for the export, which never connects.</summary>
    public static DeskContext Create(string connectionString = "Host=nowhere.invalid;Database=unused", Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<DeskContext>().UseNpgsql(connectionString);
        if (interceptor is not null)
        {
            options.AddInterceptors(interceptor);
        }

        return new DeskContext(options.Options);
    }
}

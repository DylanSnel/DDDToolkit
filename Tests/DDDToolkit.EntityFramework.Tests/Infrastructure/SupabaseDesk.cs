using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.EntityFramework.Tests.Converters;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

// A help desk, to put row access rules against a real Postgres: tickets that belong to somebody, to a
// team, or to everyone, and the comments on them, which are the ticket's own entities.

[EntityId<Guid>]
public readonly partial record struct TicketId;

[EntityId<Guid>]
public readonly partial record struct TicketCommentId;

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
}

[Entity<TicketCommentId>]
public partial class TicketComment
{
    public TicketComment(TicketCommentId id, string text) : base(id) => Text = text;

    public string Text { get; private set; }
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

/// <summary>Anybody reads a public ticket.</summary>
[RowAccess<Ticket>(RowOperations.Read)]
public static partial class PublicTicketsAreEveryones
{
    public static bool Allows(Ticket ticket, Caller caller) => ticket.IsPublic;
}

/// <summary>The desk's rules, each as the export takes it and as C# asks it.</summary>
public static class DeskRules
{
    public static readonly RowAccessRule Owners = RowAccessRule.For<Ticket>("Owners have their tickets", RowOperations.All, OwnersHaveTheirTickets.RowAccessSql);

    public static readonly RowAccessRule Teammates = RowAccessRule.For<Ticket>("Teammates read open tickets", RowOperations.Read, TeammatesReadOpenTickets.RowAccessSql);

    public static readonly RowAccessRule Public = RowAccessRule.For<Ticket>("Public tickets are everyones", RowOperations.Read, PublicTicketsAreEveryones.RowAccessSql);

    /// <summary>Whether the rules that let a caller read let <paramref name="caller"/> read <paramref name="ticket"/>, in C#.</summary>
    public static bool Reads(Ticket ticket, Caller caller, bool withPublic = true)
        => OwnersHaveTheirTickets.Allows(ticket, caller)
            || TeammatesReadOpenTickets.Allows(ticket, caller)
            || (withPublic && PublicTicketsAreEveryones.Allows(ticket, caller));
}

public sealed class DeskContext(DbContextOptions<DeskContext> options) : DbContext(options)
{
    public DbSet<Ticket> Tickets => Set<Ticket>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.HasDefaultSchema("desk");

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

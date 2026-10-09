using System.Reflection;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

/// <summary>
/// Row level security the desk's tests contribute, as a package would: whether the caller is on duty, which
/// the caller's claims say, and the open tickets whoever is on duty reads, as a set. The set asks the duty
/// question, and sorts before it by name, so a script that wrote its functions by name would create the set
/// before the question it asks.
/// </summary>
public sealed class DutyRowAccess : IRowAccessContribution
{
    public const string OnDuty = "duty/on_duty";

    public const string DutyTickets = "duty/duty_tickets";

    public string Owner => "duty";

    /// <summary>Also a restrictive policy: signed-in callers read open tickets only, whatever else lets them read one.</summary>
    public bool OpenOnly { get; init; }

    /// <summary>Also a trigger on the tickets, with the function it runs.</summary>
    public bool Stamps { get; init; }

    public RowAccessContributionResult? Contribute(DbContext context, RowAccessExport export)
    {
        if (context.Model.FindEntityType(typeof(Ticket)) is not { } tickets)
        {
            return null;
        }

        var schema = context.Model.GetDefaultSchema() ?? PostgresRowAccess.DefaultSchema;
        var id = RowAccessModel.Column(tickets, nameof(Ticket.Id));
        var open = $"{RowAccessModel.Column(tickets, nameof(Ticket.Status))} = {RowAccessModel.Stored(tickets, nameof(Ticket.Status), TicketStatus.Open)}";

        List<ContributedPolicy> policies =
        [
            new(tickets, "On duty reads the open tickets", "SELECT", RowAccessRoles.User, $"{id} = ANY (ARRAY(SELECT {{fn:{DutyTickets}}}()))", null),
        ];
        if (OpenOnly)
        {
            policies.Add(new(tickets, "Open tickets only", "SELECT", RowAccessRoles.User, open, null, Restrictive: true));
        }

        return new(
            [
                new ContributedFunction("on_duty", "", "boolean", "SELECT coalesce(({caller:claims} ->> 'on_duty')::boolean, false)", GrantTo: [RowAccessRoles.User]),
                new ContributedFunction(
                    "duty_tickets",
                    "",
                    "SETOF " + RowAccessModel.ColumnType(tickets, nameof(Ticket.Id)),
                    $"SELECT t.{id} FROM {RowAccessModel.Table(tickets)} t WHERE t.{open} AND {{fn:{OnDuty}}}()",
                    SecurityDefiner: true,
                    GrantTo: [RowAccessRoles.User]),
            ],
            policies,
            Stamps
                ?
                [
                    $"CREATE OR REPLACE FUNCTION {schema}.duty_stamp() RETURNS trigger LANGUAGE plpgsql SET search_path = '' AS $body$ BEGIN RETURN NEW; END $body$",
                    $"CREATE OR REPLACE TRIGGER duty_stamp BEFORE UPDATE ON {RowAccessModel.Table(tickets)} FOR EACH ROW EXECUTE FUNCTION {schema}.duty_stamp()",
                ]
                : []);
    }

    /// <summary>The contribution's type, its assembly and the assembly's version, as the comments of a script name them.</summary>
    public static string Source
    {
        get
        {
            var assembly = typeof(DutyRowAccess).Assembly;
            var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
            return $"{typeof(DutyRowAccess).FullName} in {assembly.GetName().Name} {version}";
        }
    }
}

/// <summary>A contribution a test writes on the spot, which records every context it was asked about.</summary>
public sealed class SpotContribution(string owner, Func<DbContext, RowAccessContributionResult?> answer) : IRowAccessContribution
{
    public List<Type> Asked { get; } = [];

    public string Owner => owner;

    public RowAccessContributionResult? Contribute(DbContext context, RowAccessExport export)
    {
        Asked.Add(context.GetType());
        return answer(context);
    }

    /// <summary>The entity type <typeparamref name="T"/> of <paramref name="context"/>'s model, an owned one included.</summary>
    public static IEntityType Of<T>(DbContext context)
        => context.Model.GetEntityTypes().Single(entity => entity.ClrType == typeof(T));
}

/// <summary>A package's contribution, generic over a class of the application's, as Membership's is over a member class.</summary>
public sealed class PackageSpot<TRow>(string owner, Func<DbContext, RowAccessContributionResult?> answer) : IRowAccessContribution
    where TRow : class
{
    public string Owner => owner;

    public RowAccessContributionResult? Contribute(DbContext context, RowAccessExport export) => answer(context);
}

/// <summary>
/// The class the Supabase build writes into the project that runs the export for a package's contribution, which
/// holds the package's class as it made it and answers for it.
/// </summary>
public sealed class MadeByTheBuild(IRowAccessContribution contribution) : IPackageRowAccessContribution
{
    public IRowAccessContribution Contribution { get; } = contribution;

    public string Owner => Contribution.Owner;

    public RowAccessContributionResult? Contribute(DbContext context, RowAccessExport export) => Contribution.Contribute(context, export);
}

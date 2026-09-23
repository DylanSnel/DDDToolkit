using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Invariants;

namespace DDDToolkit.Tests.Localization;

/// <summary>The id of a till.</summary>
[EntityId<Guid>("TILL")]
public readonly partial record struct TillId;

/// <summary>The id of one drawer in a till.</summary>
[EntityId<int>("DRAWER")]
public readonly partial record struct DrawerId;

/// <summary>
/// An aggregate whose rules name the values that broke them, so the tests can follow those values from
/// the rule to the violation, into the exception and out through a translation.
/// </summary>
[AggregateRoot<TillId>]
public partial class Till
{
    /// <summary>Opens a till that may hold at most <paramref name="limit"/>.</summary>
    public Till(TillId id, decimal limit) : base(id) => Limit = limit;

    /// <summary>The most the till may hold.</summary>
    public decimal Limit { get; private set; }

    /// <summary>What the till holds now.</summary>
    public decimal Cash { get; private set; }

    /// <summary>The drawers.</summary>
    public partial IReadOnlyList<Drawer> Drawers { get; }

    /// <summary>Puts money in.</summary>
    public void Deposit(decimal amount) => Cash += amount;

    /// <summary>Adds a drawer holding <paramref name="coins"/> coins.</summary>
    public Drawer AddDrawer(DrawerId id, int coins)
    {
        var drawer = new Drawer(id, coins);
        _drawers.Add(drawer);
        return drawer;
    }

    /// <summary>A rule that hands its values on, so a translation can place them.</summary>
    public sealed class MustStayUnderLimit : IInvariant<Till>
    {
        /// <summary>The code, public so the tests can look for it.</summary>
        public const string ViolationCode = "Till.OverLimit";

        /// <inheritdoc />
        public string Code => ViolationCode;

        /// <inheritdoc />
        public InvariantFailure? Check(Till entity)
            => entity.Cash <= entity.Limit
                ? null
                : new InvariantFailure($"A till holds at most {entity.Limit}, and this one holds {entity.Cash}.")
                    .With("Limit", entity.Limit)
                    .With("Cash", entity.Cash);
    }
}

/// <summary>A child with a rule of its own, so a child's violation can be followed through the exception.</summary>
[Entity<DrawerId>]
public partial class Drawer
{
    /// <summary>Creates a drawer.</summary>
    public Drawer(DrawerId id, int coins) : base(id) => Coins = coins;

    /// <summary>How many coins it holds.</summary>
    public int Coins { get; private set; }

    /// <summary>A rule shared in spirit by many entities, and translated per entity type.</summary>
    public sealed class MustNotBeNegative : IInvariant<Drawer>
    {
        /// <summary>The code, deliberately generic.</summary>
        public const string ViolationCode = "NotNegative";

        /// <inheritdoc />
        public string Code => ViolationCode;

        /// <inheritdoc />
        public InvariantFailure? Check(Drawer entity)
            => entity.Coins >= 0 ? null : new InvariantFailure("A drawer cannot hold fewer than no coins.").With("Coins", entity.Coins);
    }
}

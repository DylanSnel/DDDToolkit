using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// What an application that invites people may tell Tenancy: how long an invitation stays open, which has defaults.
/// A new invitation's id is made by the id itself, with its <c>Create()</c>, as every id of Tenancy's is.
/// </summary>
/// <typeparam name="TInvitationId">The application's invitation id.</typeparam>
public sealed class TenancyInvitationOptions<TInvitationId>
    where TInvitationId : struct, IEntityId, IEquatable<TInvitationId>
{
    /// <summary>How long an invitation stays open when whoever issues it does not say: seven days.</summary>
    public TimeSpan DefaultLifetime { get; set; } = TimeSpan.FromDays(7);

    /// <summary>The shortest an invitation may stay open: ten minutes.</summary>
    public TimeSpan MinLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>The longest an invitation may stay open: thirty days. A token that is never used should not stay good for ever.</summary>
    public TimeSpan MaxLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>What is wrong with the options, or <see langword="null"/> when nothing is.</summary>
    internal string? Problem()
        => MinLifetime <= TimeSpan.Zero || MinLifetime > DefaultLifetime || DefaultLifetime > MaxLifetime
            ? "the lifetimes are out of order: MinLifetime is more than nothing, and no more than DefaultLifetime, which is no more than MaxLifetime"
            : null;

    /// <summary>The options, checked: a use case made without registration still says what is wrong.</summary>
    internal TenancyInvitationOptions<TInvitationId> Checked() => Problem() is { } problem ? throw Invalid(problem) : this;

    internal static InvalidOperationException Invalid(string problem)
        => new("Tenancy's invitations cannot work with these options: " + problem + ". Set them in the configure callback of AddTenancyInvitations.");
}

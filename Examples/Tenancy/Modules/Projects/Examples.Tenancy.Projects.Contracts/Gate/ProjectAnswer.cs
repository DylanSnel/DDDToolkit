using DDDToolkit.Abstractions.Attributes;
using Examples.Tenancy.Shared.Domain.ValueObjects;

namespace Examples.Tenancy.Projects.Contracts.Gate;

/// <summary>What Projects says about the caller and one project.</summary>
/// <param name="Visible">
/// Whether the caller may see the project at all. When it may not, the others say nothing: a project of
/// another tenant, one outside the caller's reach and one that does not exist are the same answer.
/// </param>
/// <param name="Allowed">Whether the caller holds the key asked about on the project, through its crew or its unit.</param>
/// <param name="Closed">
/// Whether the project is closed, which refuses every change whatever the caller holds, until it is reopened.
/// </param>
/// <param name="Planned">
/// The days the project is planned for, or <see langword="null"/> when it has no planned range. Whoever may see
/// a project may know its plan, so it is answered with <paramref name="Visible"/>, whatever the key asked about.
/// A module that records on a project keeps what it records to these days, and refuses with a code of its own.
/// </param>
[ModuleContract]
public sealed record ProjectAnswer(bool Visible, bool Allowed, bool Closed, DateRange? Planned = null);

namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>
/// What a unit is called (<c>POST /tenancy/directory/units</c>): its name, its path from the root, such as
/// <c>Harbor Works / North / North Coast</c>, and its depth, and nothing more of it.
/// </summary>
public sealed record UnitNameInfo(Guid Id, string Name, string Path, int Depth);

namespace DDDToolkit.Supporting.Tenancy.Catalogue;

/// <summary>A kind of organization unit the application has, such as a region or a site. A label: nothing about access reads it.</summary>
/// <param name="Key">The kind's key, as a unit stores it: not blank, at most 64 characters, unique.</param>
/// <param name="Name">What the kind is called on screen.</param>
/// <param name="Order">Where it is listed; lower first.</param>
public sealed record UnitKind(string Key, string Name, int Order = 100);

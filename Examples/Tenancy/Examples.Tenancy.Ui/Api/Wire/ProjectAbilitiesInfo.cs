namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>What the caller may do to one project, as the API would decide it. Only a closed project can be reopened.</summary>
public sealed record ProjectAbilitiesInfo(bool Rename, bool Plan, bool Move, bool Close, bool Reopen, bool ManageCrew, bool ChangeOwner);

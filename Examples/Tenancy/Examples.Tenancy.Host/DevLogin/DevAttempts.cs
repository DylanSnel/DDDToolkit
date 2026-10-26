using Examples.Tenancy.Inspections.Domain.Aggregates.Inspections;
using Examples.Tenancy.Projects.Domain.Aggregates.Projects;
using DDDToolkit.Supporting.Tenancy;

namespace Examples.Tenancy.Host.DevLogin;

/// <summary>
/// The try-it presets: calls that deliberately do what the caller may not, one for each way the demonstration
/// keeps people out, and one read that is allowed, beside the same read refused. Each is a refusal or only reads,
/// so running any of them, any number of times, changes nothing.
/// </summary>
/// <remarks>
/// They cover the tenant boundary (another tenant's project, another tenant's slug), the reach of a role (a
/// project outside the caller's region, one reached only through an expired grant), keys the caller does not hold
/// on a project, an access admin who runs access and not the work, naming an owner from a crew, giving or taking
/// away a role that manages access without its keys that do, granting outside one's region, giving oneself a role
/// that manages access, the last administrator, the project owner who may neither go nor lose the lead role, a
/// role of the organization given on a crew, a project role held twice, an inspection for days outside its
/// project's planned range, the access history read with and without the key that reads it, inviting a person
/// without the key that adds a seat, accepting with a token nobody was given, a suspended seat, and calls without
/// a token or without a tenant.
/// <para>
/// Pier 7's presets assume it is open. Closed, as anyone who may close it can do from the UI, the three that would
/// change who is on its crew or what they hold there are refused with <c>projects.closed</c> instead, until it is
/// reopened.
/// </para>
/// <para>
/// They assume the roles as seeded, too. A role given from the UI first can make one succeed, and it then changes
/// what it asks for: given Crew lead at North Coast, juno makes vic crew lead on Pier 7. A freshly seeded database
/// has them all refused again.
/// </para>
/// <para>
/// A seat may suspend itself, revoke its own grant or withdraw its own placement, and its own grants that apply
/// now are its own hold of their keys, so tove's three in meadow reach the last-administrator rule, which is what
/// they show. Harbor has two administrators, ada and maud, so there either may step down, and the rule shows in
/// meadow, which has one.
/// </para>
/// </remarks>
public static class DevAttempts
{
    /// <summary>Every preset, in the order the try-it page lists them.</summary>
    public static IReadOnlyList<Attempt> All { get; } = Create();

    private static List<Attempt> Create()
    {
        var harbor = DemoData.Harbor;
        var meadow = DemoData.Meadow;

        var pierSeven = harbor.ProjectNamed("Pier 7").Id.Value;
        var bayBridge = harbor.ProjectNamed("Bay bridge").Id.Value;
        var inlandDepot = harbor.ProjectNamed("Inland depot").Id.Value;
        var gardenShed = meadow.ProjectNamed("Garden shed").Id.Value;

        return
        [
            WithTokenAndTenant(
                "other-tenants-project",
                "Open another tenant's project with your own tenant header (tove in harbor opens Garden shed)",
                DemoPeople.Tove, harbor, "GET", $"/projects/{gardenShed}", null, 404, ProjectRefusals.NotFound),
            WithTokenAndTenant(
                "other-tenants-slug",
                "Another tenant's slug in the header (rhea names meadow)",
                DemoPeople.Rhea, meadow, "GET", "/me", null, 403, TenancyRefusals.NotSeated),
            WithTokenAndTenant(
                "outside-your-region",
                "A project outside your region (rhea opens Bay bridge)",
                DemoPeople.Rhea, harbor, "GET", $"/projects/{bayBridge}", null, 404, ProjectRefusals.NotFound),
            WithTokenAndTenant(
                "through-an-expired-grant",
                "A project reached only through your expired grant (vic opens Inland depot)",
                DemoPeople.Vic, harbor, "GET", $"/projects/{inlandDepot}", null, 404, ProjectRefusals.NotFound),
            WithTokenAndTenant(
                "close-as-observer",
                "Close a project you only observe (vic closes Pier 7; needs projects.close)",
                DemoPeople.Vic, harbor, "POST", $"/projects/{pierSeven}/close", null, 403, ProjectRefusals.NotPermitted),
            WithTokenAndTenant(
                "rename-as-access-admin",
                "Do the work as an access admin (maud renames Pier 7; she runs access and sees the project, and holds no projects.edit)",
                DemoPeople.Maud, harbor, "PUT", $"/projects/{pierSeven}/name", new { name = "Pier Seven" }, 403, ProjectRefusals.NotPermitted),
            WithTokenAndTenant(
                "crew-role-without-crew-management",
                "Give a project role without crew management (juno gives vic Crew lead on Pier 7; needs projects.crew.manage)",
                DemoPeople.Juno, harbor, "POST", $"/projects/{pierSeven}/crew/{harbor.SeatOf(DemoPeople.Vic).Value}/roles",
                new { roleId = harbor.ProjectRoles[SampleCatalogue.CrewLead].Value }, 403, ProjectRefusals.NotPermitted),
            WithTokenAndTenant(
                "organization-role-on-a-crew",
                "Give a role of the organization on a crew (leo gives vic Area manager on Pier 7; a crew holds project roles)",
                DemoPeople.Leo, harbor, "POST", $"/projects/{pierSeven}/crew/{harbor.SeatOf(DemoPeople.Vic).Value}/roles",
                new { roleId = harbor.Roles[SampleCatalogue.AreaManager].Value }, 400, ProjectRefusals.RoleNotForMembers),
            WithTokenAndTenant(
                "crew-role-held-twice",
                "Give a crew member a project role it holds already (leo gives juno Surveyor on Pier 7 again)",
                DemoPeople.Leo, harbor, "POST", $"/projects/{pierSeven}/crew/{harbor.SeatOf(DemoPeople.Juno).Value}/roles",
                new { roleId = harbor.ProjectRoles[SampleCatalogue.Surveyor].Value }, 409, ProjectRefusals.CrewRoleHeld),
            WithTokenAndTenant(
                "inspection-outside-the-planned-range",
                "Record an inspection for days outside the project's planned range (rhea records one on Inland depot for a week in 2000; Inspections refuses with what Projects' gate answered)",
                DemoPeople.Rhea, harbor, "POST", $"/projects/{inlandDepot}/inspections",
                new { title = "Foundation trench checked", from = "2000-01-03", until = "2000-01-07" }, 409, InspectionRefusals.OutsidePlannedRange),
            WithTokenAndTenant(
                "owner-change-from-the-crew",
                "Name another owner from the crew (leo, owner and crew lead of Pier 7, names juno; only the organization names an owner)",
                DemoPeople.Leo, harbor, "PUT", $"/projects/{pierSeven}/owner",
                new { seatId = harbor.SeatOf(DemoPeople.Juno).Value }, 403, ProjectRefusals.NotPermitted),
            WithTokenAndTenant(
                "grant-above-your-rights",
                "Grant an org role above your rights (rhea grants Access admin to leo at North Coast)",
                DemoPeople.Rhea, harbor, "POST", $"/tenancy/seats/{harbor.SeatOf(DemoPeople.Leo).Value}/grants",
                new { unitId = harbor.UnitNamed("North Coast").Value, roleId = harbor.Roles[SampleCatalogue.AccessAdmin].Value },
                403, TenancyRefusals.GrantExceedsOwn),
            WithTokenAndTenant(
                "grant-outside-your-region",
                "Grant outside your region (rhea grants Observer to tove at South Bay)",
                DemoPeople.Rhea, harbor, "POST", $"/tenancy/seats/{harbor.SeatOf(DemoPeople.Tove).Value}/grants",
                new { unitId = harbor.UnitNamed("South Bay").Value, roleId = harbor.Roles[SampleCatalogue.Observer].Value },
                403, TenancyRefusals.NotPermitted),
            WithTokenAndTenant(
                "give-a-role-that-manages-access",
                "Give a role that manages access without its keys (hana gives leo Area manager at North Coast)",
                DemoPeople.Hana, harbor, "POST", $"/tenancy/seats/{harbor.SeatOf(DemoPeople.Leo).Value}/grants",
                new { unitId = harbor.UnitNamed("North Coast").Value, roleId = harbor.Roles[SampleCatalogue.AreaManager].Value },
                403, TenancyRefusals.GrantExceedsOwn),
            WithTokenAndTenant(
                "give-access-admin-from-the-people-office",
                "Give Access admin from the people office (hana gives leo Access admin at North Coast)",
                DemoPeople.Hana, harbor, "POST", $"/tenancy/seats/{harbor.SeatOf(DemoPeople.Leo).Value}/grants",
                new { unitId = harbor.UnitNamed("North Coast").Value, roleId = harbor.Roles[SampleCatalogue.AccessAdmin].Value },
                403, TenancyRefusals.GrantExceedsOwn),
            WithTokenAndTenant(
                "give-crew-lead-from-the-people-office",
                "Give Crew lead at a unit without crew management (hana gives leo Crew lead at North Coast; needs projects.crew.manage)",
                DemoPeople.Hana, harbor, "POST", $"/tenancy/seats/{harbor.SeatOf(DemoPeople.Leo).Value}/grants",
                new { unitId = harbor.UnitNamed("North Coast").Value, roleId = harbor.Roles[SampleCatalogue.CrewLead].Value },
                403, TenancyRefusals.GrantExceedsOwn),
            WithTokenAndTenant(
                "take-away-a-role-that-manages-access",
                "Take away a role that manages access without its keys (hana revokes rhea's Area manager at North)",
                DemoPeople.Hana, harbor, "DELETE",
                $"/tenancy/seats/{harbor.SeatOf(DemoPeople.Rhea).Value}/grants/{harbor.UnitNamed("North").Value}/{harbor.Roles[SampleCatalogue.AreaManager].Value}",
                null, 403, TenancyRefusals.GrantExceedsOwn),
            WithTokenAndTenant(
                "appoint-yourself",
                "Give yourself a role that manages access (hana gives herself Area manager at Harbor Works)",
                DemoPeople.Hana, harbor, "POST", $"/tenancy/seats/{harbor.SeatOf(DemoPeople.Hana).Value}/grants",
                new { unitId = harbor.Root.Value, roleId = harbor.Roles[SampleCatalogue.AreaManager].Value },
                403, TenancyRefusals.SelfAppointment),
            WithTokenAndTenant(
                "appoint-yourself-holding-its-key",
                "Give yourself a role whose keys you hold (rhea gives herself People office at North)",
                DemoPeople.Rhea, harbor, "POST", $"/tenancy/seats/{harbor.SeatOf(DemoPeople.Rhea).Value}/grants",
                new { unitId = harbor.UnitNamed("North").Value, roleId = harbor.Roles[SampleCatalogue.PeopleOffice].Value },
                403, TenancyRefusals.SelfAppointment),
            WithTokenAndTenant(
                "only-administrator-revokes",
                "Remove the last admin: tove in meadow revokes her own Tenant admin grant",
                DemoPeople.Tove, meadow, "DELETE",
                $"/tenancy/seats/{meadow.Administrator.Id.Value}/grants/{meadow.Root.Value}/{meadow.AdministratorsRole.Value}",
                null, 409, TenancyRefusals.LastAdmin),
            WithTokenAndTenant(
                "only-administrator-suspends",
                "Remove the last admin: tove in meadow suspends her own seat",
                DemoPeople.Tove, meadow, "POST", $"/tenancy/seats/{meadow.Administrator.Id.Value}/suspend", null, 409, TenancyRefusals.LastAdmin),
            WithTokenAndTenant(
                "only-administrator-withdraws",
                "Remove the last admin: tove in meadow withdraws her root placement",
                DemoPeople.Tove, meadow, "DELETE", $"/tenancy/seats/{meadow.Administrator.Id.Value}/placements/{meadow.Root.Value}",
                null, 409, TenancyRefusals.LastAdmin),
            WithTokenAndTenant(
                "remove-the-owner",
                "Remove the project owner from the crew (leo removes himself from Pier 7)",
                DemoPeople.Leo, harbor, "DELETE", $"/projects/{pierSeven}/crew/{harbor.SeatOf(DemoPeople.Leo).Value}", null, 409, ProjectRefusals.OwnerProtected),
            WithTokenAndTenant(
                "take-the-owners-lead-role",
                "Take the lead role from the project owner (leo takes his own Crew lead role on Pier 7)",
                DemoPeople.Leo, harbor, "DELETE",
                $"/projects/{pierSeven}/crew/{harbor.SeatOf(DemoPeople.Leo).Value}/roles/{harbor.ProjectRoles[SampleCatalogue.CrewLead].Value}",
                null, 409, ProjectRefusals.OwnerProtected),
            WithTokenAndTenant(
                "history-with-the-key",
                "Read the access history with the key that reads it (maud, an access admin, holds tenancy.history.view for all of harbor)",
                DemoPeople.Maud, harbor, "GET", "/tenancy/history?size=5", null, 200, null),
            WithTokenAndTenant(
                "history-without-the-key",
                "Read the access history without the key (rhea runs North, and holds no tenancy.history.view)",
                DemoPeople.Rhea, harbor, "GET", "/tenancy/history?size=5", null, 403, TenancyRefusals.NotPermitted),
            WithTokenAndTenant(
                "invite-without-seat-management",
                "Invite a person without seat management (hana gives roles, and adds no seat; needs tenancy.seats.manage for all of harbor)",
                DemoPeople.Hana, harbor, "POST", "/tenancy/invitations",
                new { address = "wren@example.test", unitId = harbor.UnitNamed("North Coast").Value, roleId = harbor.Roles[SampleCatalogue.Observer].Value },
                403, TenancyRefusals.NotPermitted),
            new(
                "accept-an-invitation-nobody-sent",
                "Accept an invitation with a token nobody was given (leo, signed in and naming no tenant, as accepting does)",
                DemoPeople.Leo.Key, null, SendToken: true, SendTenant: false, "POST", "/invitations/accept",
                new { token = "a-token-nobody-was-given" }, 404, TenancyRefusals.InvitationNotFound),
            WithTokenAndTenant(
                "suspended-seat",
                "Act with a suspended seat (seth asks who he is)",
                DemoPeople.Seth, harbor, "GET", "/me", null, 403, TenancyRefusals.SeatSuspended),
            new(
                "no-token",
                "Call without a token (rhea's projects in harbor, asked anonymously)",
                DemoPeople.Rhea.Key, harbor.Slug, SendToken: false, SendTenant: true, "GET", "/projects", null, 401, null),
            new(
                "no-tenant-header",
                "Call without the header (rhea asks who she is, naming no tenant)",
                DemoPeople.Rhea.Key, null, SendToken: true, SendTenant: false, "GET", "/me", null, 400, TenancyRefusals.TenantRequired),
        ];
    }

    /// <summary>An attempt made as <paramref name="person"/>, with their token, in <paramref name="tenant"/>.</summary>
    private static Attempt WithTokenAndTenant(
        string id,
        string title,
        DemoPerson person,
        DemoTenant tenant,
        string method,
        string path,
        object? body,
        int expectedStatus,
        string? expectedCode)
        => new(id, title, person.Key, tenant.Slug, SendToken: true, SendTenant: true, method, path, body, expectedStatus, expectedCode);
}

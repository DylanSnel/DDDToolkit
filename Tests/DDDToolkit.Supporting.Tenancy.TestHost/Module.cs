using DDDToolkit.Abstractions.Attributes;

// The application's tenancy module. The package declares no module of its own: once an application
// references it, it is part of whichever module declares the classes, and that is this one.
[assembly: Module("Tenancy")]

// Tenancy's managers closed over this module's classes, which the toolkit's generator writes here and every test
// project takes them by: TenantManager, OrganizationManager, SeatManager, InvitationManager and the access manager,
// under the package's names. The role manager would be RoleManager; this host gives it a name of its own,
// HostRoleManager, with the line an application writes when two of its modules declare Tenancy's classes, so that
// line is built and used through a reference in all three of Tenancy's test projects.
[assembly: TemplateFacadeName("RoleManager", "HostRoleManager")]

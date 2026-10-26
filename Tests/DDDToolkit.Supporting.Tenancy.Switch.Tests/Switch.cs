using DDDToolkit.Supporting.Tenancy;

// The one line of Tenancy's classes this application writes. The generator writes a Tenant, an Organization, an
// OrganizationUnit, a Role and a Seat, each as the package ships it, and their ids, TenantId, OrganizationUnitId,
// RoleId and SeatId, in the root namespace, Shop. A class this application declared itself would win, and the switch
// would write the rest around it.
[assembly: GenerateTenancyClasses]

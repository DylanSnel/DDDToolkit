namespace Examples.Tenancy.Projects.Application.StoredProjects;

/// <summary>
/// Where Projects' queries, and its access checks, read. The application declares it; the infrastructure project
/// implements it and registers it, scoped.
/// </summary>
/// <remarks>
/// Every read gets storage of its own, and none uses the unit of work the request's commands save through. A
/// request may send several queries side by side, as the resolvers of one GraphQL request do, and one unit of
/// work runs one query at a time: two queries on it would meet. So the port hands out nothing but readings, and a
/// reading lives as long as the one query that opened it.
/// <para>
/// Public, because the infrastructure project implements it and a handler's constructor names it. No route
/// names it: a route only sends a request.
/// </para>
/// </remarks>
public interface IProjectReads
{
    /// <summary>
    /// Opens a reading: the projects and Tenancy's rows on a context of its own, for one query. The caller
    /// disposes it, <c>await using</c>, when the query has run.
    /// </summary>
    IProjectReading Open();
}

using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Examples.Tenancy.Projects.Api.Rest;

/// <summary>
/// A project's version as HTTP carries it: the strong entity tag <c>GET /projects/{id}</c> answers in its
/// <c>ETag</c>, and a change sends back in its <c>If-Match</c>. Every feature's routes that change a project read
/// it here, so all of them take the same header in the same spelling; it is no feature's own, which is why it
/// sits beside the features and not in one of them.
/// </summary>
/// <remarks>
/// The headers are read and written through ASP.NET Core's typed headers, so what a tag is, weak or strong, one
/// or a list, is the framework's reading and not this project's.
/// </remarks>
internal static class ProjectVersions
{
    /// <summary>
    /// The version a change says its caller last read the project at, from the request's <c>If-Match</c>.
    /// <see langword="null"/> without the header, and then the change is made to the project as it is now.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <exception cref="BadHttpRequestException">
    /// The header is there and is not one strong tag of a version, such as <c>"7"</c>: a weak tag, a list, a star
    /// or anything else. The host answers it as 400 <c>invalid-request</c>.
    /// </exception>
    public static long? Expected(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Headers.IfMatch.Count == 0)
        {
            return null;
        }

        // A header the framework cannot read as entity tags comes back as no tag at all, and is refused with the rest.
        return request.GetTypedHeaders().IfMatch is [{ IsWeak: false } tag]
            && long.TryParse(tag.Tag.AsSpan().Trim('"'), NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            ? version
            : throw new BadHttpRequestException("If-Match must be the version of the project as its ETag gave it: one strong tag, such as \"7\".");
    }

    /// <summary>Writes <paramref name="version"/> as the response's <c>ETag</c>, a strong tag.</summary>
    /// <param name="response">The response of the route that read the project.</param>
    /// <param name="version">The project's version.</param>
    public static void WriteTo(HttpResponse response, long version)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.GetTypedHeaders().ETag = new EntityTagHeaderValue(string.Create(CultureInfo.InvariantCulture, $"\"{version}\""));
    }
}

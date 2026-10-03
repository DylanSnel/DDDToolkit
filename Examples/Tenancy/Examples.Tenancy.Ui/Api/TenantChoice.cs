namespace Examples.Tenancy.Ui.Api;

/// <summary>Which tenant a call names in its <c>Tenant</c> header, if any.</summary>
public sealed class TenantChoice
{
    private TenantChoice(bool fromSession, string? slug)
    {
        FromSession = fromSession;
        Slug = slug;
    }

    /// <summary>The tenant the session has picked.</summary>
    public static TenantChoice Session { get; } = new(fromSession: true, slug: null);

    /// <summary>No <c>Tenant</c> header.</summary>
    public static TenantChoice None { get; } = new(fromSession: false, slug: null);

    /// <summary>
    /// The tenant <paramref name="slug"/> names, trimmed, as the try-it form overrides it. A blank slug sends no
    /// header, so leaving the field empty is how to see the API ask for one.
    /// </summary>
    public static TenantChoice Of(string? slug)
        => string.IsNullOrWhiteSpace(slug) ? None : new(fromSession: false, slug.Trim());

    /// <summary>Whether the session's tenant is sent.</summary>
    public bool FromSession { get; }

    /// <summary>The slug sent when <see cref="FromSession"/> is not set; <see langword="null"/> for none.</summary>
    public string? Slug { get; }
}

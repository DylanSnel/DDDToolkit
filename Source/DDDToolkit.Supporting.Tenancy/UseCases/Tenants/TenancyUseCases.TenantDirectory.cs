using System.Buffers.Text;
using System.Text;
using System.Text.RegularExpressions;
using DDDToolkit.Access;

namespace DDDToolkit.Supporting.Tenancy.UseCases;

public abstract partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>
    /// The tenants of the application, for its operators: every tenant by slug, a page at a time, each with how
    /// many of its seats are in use. It reads, and changes nothing.
    /// <para>
    /// Only an operator asks: a signed-in user whose token carries one of
    /// <see cref="TenancyOptions{TTenantId, TSeatId, TUnitId, TRoleId}.OperatorTokenRoles"/>. An operator holds no
    /// seat and acts in no tenant, so this is the one use case that asks the toolkit's own caller rather than the
    /// Tenancy caller. It reads as that caller, never as the system: on a database that keeps tenants apart by
    /// itself, such as Postgres with row level security, the operator's own database role and its policies decide
    /// what it reads, and a host that mapped no such role reads nothing.
    /// </para>
    /// <para>
    /// The application's own work that has to visit every tenant, a sweep say, does not go through here: it asks
    /// which tenants there are in a scope of its own, and then works in each.
    /// </para>
    /// </summary>
    /// <param name="store">Where it reads.</param>
    /// <param name="options">Which token roles are operators'.</param>
    /// <param name="callers">Who is calling, as the toolkit says.</param>
    public sealed class TenantDirectory(
        IStore store,
        TenancyOptions<TTenantId, TSeatId, TUnitId, TRoleId> options,
        ICallerAccessor callers)
    {
        /// <summary>How many tenants a page holds at most.</summary>
        public const int MostPerPage = 200;

        /// <summary>
        /// A page of the tenants, by slug: those after the last tenant of the page before, or the first ones.
        /// </summary>
        /// <param name="after">
        /// <see cref="TenantDirectoryPage.Next"/> of the page before, or <see langword="null"/> for the first page.
        /// It is a marker the directory made, and means nothing to anyone else.
        /// </param>
        /// <param name="size">How many tenants the page holds: 1 to <see cref="MostPerPage"/>.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <exception cref="Exceptions.RefusalException">
        /// <c>tenancy.operators-only</c> for anyone but an operator, asked before anything else, so nobody else
        /// learns what a page holds; <c>tenancy.page-size-invalid</c>, with <c>Max</c>; <c>tenancy.cursor-invalid</c>
        /// for a marker the directory did not make. Each of the last two names its input in the argument
        /// <c>Field</c>: <c>size</c> and <c>after</c>.
        /// </exception>
        public async Task<TenantDirectoryPage> ListAsync(string? after, int size, CancellationToken cancellationToken)
        {
            if (!options.IsOperator(callers.Current))
            {
                throw TenancyRefusals.Refuse(TenancyRefusals.OperatorsOnly);
            }

            if (size is < 1 or > MostPerPage)
            {
                throw TenancyRefusals.Refuse(TenancyRefusals.PageSizeInvalid, ("Max", MostPerPage));
            }

            var afterSlug = after is null ? null : PageMarker.Read(after) ?? throw TenancyRefusals.Refuse(TenancyRefusals.CursorInvalid);

            // One more than the page holds: whether there is a page after this one is known without counting.
            var found = await store.ListTenantsAsync(afterSlug, size + 1, cancellationToken).ConfigureAwait(false);
            var items = found.Count > size ? found.Take(size).ToArray() : [.. found];

            return new TenantDirectoryPage(items, found.Count > size ? PageMarker.Write(items[^1].Slug) : null);
        }
    }

    /// <summary>
    /// The marker a page of the tenants' directory continues from: the slug of the last tenant of the page before,
    /// written so that a client passes it on as it is and makes nothing of it.
    /// </summary>
    private static class PageMarker
    {
        /// <summary>The marker for the page after the tenant with <paramref name="slug"/>.</summary>
        public static string Write(string slug) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(slug));

        /// <summary>The slug <paramref name="marker"/> was written from, or <see langword="null"/> when it is no marker of the directory's.</summary>
        public static string? Read(string marker)
        {
            // No slug is longer than a slug may be, so neither is its marker: a longer one is not read at all.
            if (marker.Length == 0 || marker.Length > Base64Url.GetEncodedLength(TenantSlug.MaxLength) || !Base64Url.IsValid(marker))
            {
                return null;
            }

            var slug = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(marker));
            return Regex.IsMatch(slug, TenantSlug.Pattern, RegexOptions.CultureInvariant) ? slug : null;
        }
    }
}

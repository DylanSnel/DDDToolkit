namespace Examples.Tenancy.Ui.Api.TryIt;

/// <summary>Every action of the try-it form, in the order it lists them.</summary>
/// <remarks>
/// Each is a route of the API the screens also use, or could, so the form can do anything the screens can and
/// anything they would not offer: act in another tenant, as nobody, on an id that is not one. Blank ids in a body
/// are sent as JSON null, which the API refuses as an unreadable request.
/// <para>
/// The three questions to the directory ask what one id is called. An id of another tenant is answered an empty
/// list, exactly as an id of nothing is: the form shows that the answer does not say which.
/// </para>
/// <para>
/// A crew is changed in four steps of its own: a seat is put on it, with a project role or without; a member is
/// given a project role, next to the ones it holds; a project role is taken from a member, who stays; and a member
/// is taken off it. A crew holds the tenant's project roles, never its roles of the organization: an id of one of
/// those is one of the refusals to try.
/// </para>
/// <para>
/// An action is told apart by the name of its text in the UI's resource files, where what the form calls it is
/// written in each language: the form is the UI's own, so its words follow the language switch as every page's do.
/// </para>
/// </remarks>
public static class TryItActions
{
    /// <summary>The actions.</summary>
    public static IReadOnlyList<TryItAction> All { get; } =
    [
        new("try.action.open-project", HttpMethod.Get, "/projects/{projectId}", [TryItField.Project]),
        new("try.action.check-key", HttpMethod.Get, "/access/projects/{projectId}?key={key}", [TryItField.Project, TryItField.Key]),
        new("try.action.units-with-key", HttpMethod.Get, "/access/units?key={key}", [TryItField.Key]),
        new("try.action.seat-name", HttpMethod.Post, "/tenancy/directory/seats", [TryItField.Seat],
            input => new { ids = new[] { SampleApi.Blank(input.Seat) } }, OnlyAsks: true),
        new("try.action.unit-path", HttpMethod.Post, "/tenancy/directory/units", [TryItField.Unit],
            input => new { ids = new[] { SampleApi.Blank(input.Unit) } }, OnlyAsks: true),
        new("try.action.role-name", HttpMethod.Post, "/tenancy/directory/roles", [TryItField.Role],
            input => new { ids = new[] { SampleApi.Blank(input.Role) } }, OnlyAsks: true),
        new("try.action.rename-project", HttpMethod.Put, "/projects/{projectId}/name", [TryItField.Project, TryItField.Name],
            input => new { name = input.Name }),
        new("try.action.move-project", HttpMethod.Put, "/projects/{projectId}/unit", [TryItField.Project, TryItField.Unit],
            input => new { unitId = SampleApi.Blank(input.Unit) }),
        new("try.action.close-project", HttpMethod.Post, "/projects/{projectId}/close", [TryItField.Project]),
        new("try.action.reopen-project", HttpMethod.Post, "/projects/{projectId}/reopen", [TryItField.Project]),
        new("try.action.record-inspection", HttpMethod.Post, "/projects/{projectId}/inspections", [TryItField.Project, TryItField.Title],
            input => new { title = input.Title }),
        new("try.action.add-to-crew", HttpMethod.Post, "/projects/{projectId}/crew", [TryItField.Project, TryItField.Seat, TryItField.ProjectRole, TryItField.Until],
            input => new { seatId = SampleApi.Blank(input.Seat), roleId = SampleApi.Blank(input.ProjectRole), until = input.UntilInstant }),
        new("try.action.give-crew-role", HttpMethod.Post, "/projects/{projectId}/crew/{seatId}/roles", [TryItField.Project, TryItField.Seat, TryItField.ProjectRole, TryItField.Until],
            input => new { roleId = SampleApi.Blank(input.ProjectRole), until = input.UntilInstant }),
        new("try.action.take-crew-role", HttpMethod.Delete, "/projects/{projectId}/crew/{seatId}/roles/{projectRoleId}", [TryItField.Project, TryItField.Seat, TryItField.ProjectRole]),
        new("try.action.remove-from-crew", HttpMethod.Delete, "/projects/{projectId}/crew/{seatId}", [TryItField.Project, TryItField.Seat]),
        new("try.action.change-owner", HttpMethod.Put, "/projects/{projectId}/owner", [TryItField.Project, TryItField.Seat],
            input => new { seatId = SampleApi.Blank(input.Seat) }),
        new("try.action.grant-org-role", HttpMethod.Post, "/tenancy/seats/{seatId}/grants", [TryItField.Seat, TryItField.Unit, TryItField.Role, TryItField.Until, TryItField.Reason],
            input => new { unitId = SampleApi.Blank(input.Unit), roleId = SampleApi.Blank(input.Role), until = input.UntilInstant, reason = SampleApi.Blank(input.Reason) },
            OrganizationRole: true),
        new("try.action.revoke-org-role", HttpMethod.Delete, "/tenancy/seats/{seatId}/grants/{unitId}/{roleId}", [TryItField.Seat, TryItField.Unit, TryItField.Role],
            OrganizationRole: true),
        new("try.action.suspend-seat", HttpMethod.Post, "/tenancy/seats/{seatId}/suspend", [TryItField.Seat]),
        new("try.action.withdraw-placement", HttpMethod.Delete, "/tenancy/seats/{seatId}/placements/{unitId}", [TryItField.Seat, TryItField.Unit]),
    ];

    /// <summary>The action whose text is named <paramref name="text"/>, such as <c>try.action.open-project</c>.</summary>
    /// <exception cref="ArgumentException">There is no such action.</exception>
    public static TryItAction ByText(string text)
        => All.FirstOrDefault(action => action.Text == text)
           ?? throw new ArgumentException("There is no try-it action whose text is named '" + text + "'.", nameof(text));
}

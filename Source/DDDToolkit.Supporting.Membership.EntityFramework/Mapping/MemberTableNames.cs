namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// The names <c>HasMembers</c> gives the two member tables of one resource, for an application that has names
/// of its own for them, or tables that were there before:
/// <code>
/// modelBuilder.Entity&lt;Folder&gt;().HasMembers(folder =&gt; folder.Staff, folder =&gt; folder.Keeper,
///     new MemberTableNames("FolderStaff", "FolderStaffRoles", MemberColumn: "StaffCode"));
/// </code>
/// <para>
/// They are one resource's names. An application with several kinds of resource names each one's tables where
/// it maps that resource, and nothing here is shared between two.
/// </para>
/// <para>
/// Everything else, every other column, the keys and the index, is named as the context names everything, by
/// Entity Framework or by a naming convention on its options. Whatever the tables and columns end up being
/// called, the access questions and the database's functions read the names from the model.
/// </para>
/// </summary>
/// <param name="Members">
/// The table of the members. Left out, it is named after the resource and the property that holds its members:
/// <c>DocumentShares</c> for <c>Document.Shares</c>.
/// </param>
/// <param name="Roles">
/// The table of the roles members hold. Left out, it is named after the member class: <c>DocumentShareRoles</c>
/// for <c>DocumentShare</c>.
/// </param>
/// <param name="MemberColumn">
/// The column that holds who the member is, for an application whose word for a member is its own:
/// <c>UserId</c>, <c>StaffCode</c>. Left out, it is named as the context names the property <c>MemberId</c>.
/// </param>
public sealed record MemberTableNames(string? Members = null, string? Roles = null, string? MemberColumn = null);

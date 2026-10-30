namespace DDDToolkit.Analyzers.Tests.Diagnostics;

/// <summary>
/// <c>IsAuthenticated</c> on <c>ICurrentUser</c>, on <c>IRowUser</c> and on <c>CurrentUserSnapshot</c> carries
/// <c>[MemberNotNullWhen(true, nameof(Id))]</c>. Code that tested it reads <c>Id.Value</c> without CS8629. Code that did
/// not test it still gets the warning.
/// </summary>
public class CurrentUserNullabilityTests
{
    [Theory]
    [InlineData("ICurrentUser")]
    [InlineData("IRowUser")]
    [InlineData("CurrentUserSnapshot")]
    public void After_IsAuthenticated_answered_true_the_id_has_a_value(string type)
    {
        var result = Compile(type,
            """
            if (!user.IsAuthenticated)
            {
                throw new InvalidOperationException("Only a signed-in user gets here.");
            }

            Guid id = user.Id.Value;
            return id;
            """);

        result.CompilationDiagnostics.Should().NotContain(diagnostic => diagnostic.Id == "CS8629", "IsAuthenticated answered true, so Id has a value");
    }

    [Theory]
    [InlineData("ICurrentUser")]
    [InlineData("IRowUser")]
    [InlineData("CurrentUserSnapshot")]
    public void Without_the_test_the_id_may_be_null(string type)
    {
        var result = Compile(type,
            """
            Guid id = user.Id.Value;
            return id;
            """);

        result.CompilationDiagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == "CS8629", "nothing said that Id has a value");
    }

    [Fact]
    public void RequireId_answers_the_id_as_a_guid()
    {
        var result = Compile("ICurrentUser", "return user.RequireId();");

        result.CompilationDiagnostics.Should().NotContain(diagnostic => diagnostic.Severity >= Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);
    }

    private static GeneratorRunOutcome Compile(string type, string body)
        => GeneratorTestHost.Create(
            $$"""
            using System;
            using DDDToolkit.Abstractions.Access;

            namespace Sample;

            public static class Reader
            {
                public static Guid IdOf({{type}} user)
                {
                    {{body}}
                }
            }
            """).RunCore().ShouldCompile();
}

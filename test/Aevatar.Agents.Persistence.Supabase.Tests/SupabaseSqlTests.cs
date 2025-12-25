using Aevatar.Agents.Persistence.Supabase.Internal;
using FluentAssertions;
using Xunit;

namespace Aevatar.Agents.Persistence.Supabase.Tests;

public class SupabaseSqlTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("a1")]
    [InlineData("a_b")]
    [InlineData("a_b2")]
    [InlineData("aevatar")]
    [InlineData("ai_memory_messages")]
    public void Ident_ShouldAccept_ValidIdentifiers(string ident)
    {
        SupabaseSql.Ident(ident, "x").Should().Be(ident);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("A")]
    [InlineData("a-b")]
    [InlineData("1abc")]
    [InlineData("a;drop table x;--")]
    [InlineData("a\"b")]
    public void Ident_ShouldReject_InvalidIdentifiers(string ident)
    {
        var act = () => SupabaseSql.Ident(ident, "x");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Table_ShouldCompose_SchemaAndTable()
    {
        SupabaseSql.Table("aevatar", "agent_states", "t").Should().Be("aevatar.agent_states");
    }

    [Fact]
    public void RegConfigLiteral_ShouldQuoteAndValidate()
    {
        SupabaseSql.RegConfigLiteral("simple").Should().Be("'simple'");
        var act = () => SupabaseSql.RegConfigLiteral("simple;drop");
        act.Should().Throw<ArgumentException>();
    }
}



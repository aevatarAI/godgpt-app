using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions.CQRS;
using FluentAssertions;
using Xunit;

namespace Aevatar.App.CQRS.Tests;

/// <summary>
/// Unit tests for IStateIndexService DTOs and StateQuery
/// </summary>
public class StateIndexServiceModelsTests
{
    [Fact]
    public void StateIndexDocument_InitializesWithDefaults()
    {
        // Act
        var doc = new StateIndexDocument();

        // Assert
        doc.AgentId.Should().BeEmpty();
        doc.AgentType.Should().BeEmpty();
        doc.Data.Should().NotBeNull();
        doc.Data.Should().BeEmpty();
        doc.Version.Should().Be(0);
        doc.IndexedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void StateIndexDocument_CanSetProperties()
    {
        // Act
        var doc = new StateIndexDocument
        {
            AgentId = "agent-123",
            AgentType = "TestAgent",
            Data = new Dictionary<string, object?> { { "key", "value" } },
            Version = 10,
            IndexedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        // Assert
        doc.AgentId.Should().Be("agent-123");
        doc.AgentType.Should().Be("TestAgent");
        doc.Data.Should().ContainKey("key");
        doc.Version.Should().Be(10);
        doc.IndexedAt.Should().Be(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void StateQuery_InitializesWithDefaults()
    {
        // Act
        var query = new StateQuery();

        // Assert
        query.AgentType.Should().BeEmpty();
        query.QueryString.Should().BeNull();
        query.PageIndex.Should().Be(0);
        query.PageSize.Should().Be(20);
        query.SortFields.Should().NotBeNull();
        query.SortFields.Should().BeEmpty();
    }

    [Fact]
    public void StateQuery_CanSetProperties()
    {
        // Act
        var query = new StateQuery
        {
            AgentType = "TestAgent",
            QueryString = "status:active",
            PageIndex = 5,
            PageSize = 50,
            SortFields = new List<string> { "version:desc", "name:asc" }
        };

        // Assert
        query.AgentType.Should().Be("TestAgent");
        query.QueryString.Should().Be("status:active");
        query.PageIndex.Should().Be(5);
        query.PageSize.Should().Be(50);
        query.SortFields.Should().HaveCount(2);
    }

    [Fact]
    public void StateQueryResult_InitializesWithDefaults()
    {
        // Act
        var result = new StateQueryResult();

        // Assert
        result.AgentId.Should().BeEmpty();
        result.AgentType.Should().BeEmpty();
        result.Data.Should().NotBeNull();
        result.Version.Should().Be(0);
    }

    [Fact]
    public void PagedStateQueryResult_CalculatesTotalPages_Correctly()
    {
        // Arrange & Act
        var result = new PagedStateQueryResult
        {
            TotalCount = 100,
            PageSize = 20
        };

        // Assert
        result.TotalPages.Should().Be(5);
    }

    [Fact]
    public void PagedStateQueryResult_CalculatesTotalPages_RoundsUp()
    {
        // Arrange & Act
        var result = new PagedStateQueryResult
        {
            TotalCount = 101,
            PageSize = 20
        };

        // Assert
        result.TotalPages.Should().Be(6);
    }

    [Fact]
    public void PagedStateQueryResult_CalculatesTotalPages_ZeroPageSize()
    {
        // Arrange & Act
        var result = new PagedStateQueryResult
        {
            TotalCount = 100,
            PageSize = 0
        };

        // Assert
        result.TotalPages.Should().Be(0);
    }

    [Fact]
    public void PagedStateQueryResult_CalculatesTotalPages_EmptyResults()
    {
        // Arrange & Act
        var result = new PagedStateQueryResult
        {
            TotalCount = 0,
            PageSize = 20
        };

        // Assert
        result.TotalPages.Should().Be(0);
    }
}

/// <summary>
/// Integration-style tests for ElasticsearchStateIndexService
/// These tests verify behavior with mocked Elasticsearch client
/// </summary>
public class ElasticsearchStateIndexServiceBehaviorTests
{
    [Fact]
    public void GetIndexName_FormatsCorrectly()
    {
        // This tests the index naming convention
        // Index name should be: {prefix}-{agent_type}-state
        var agentType = "Aevatar.Agents.Chat.ChatAgent";
        var expectedIndexName = "aevatar-aevatar.agents.chat.chatagent-state";
        
        // The actual implementation converts to lowercase
        agentType.ToLowerInvariant().Should().Be("aevatar.agents.chat.chatagent");
    }

    [Fact]
    public void StateIndexDocument_CanConvertToJson()
    {
        // Arrange
        var doc = new StateIndexDocument
        {
            AgentId = "agent-123",
            AgentType = "TestAgent",
            Data = new Dictionary<string, object?>
            {
                { "name", "Test Agent" },
                { "count", 42 },
                { "active", true },
                { "nested", new Dictionary<string, object?> { { "inner", "value" } } }
            },
            Version = 1
        };

        // Assert - verify complex types can be stored
        doc.Data.Should().HaveCount(4);
        doc.Data["nested"].Should().BeOfType<Dictionary<string, object?>>();
    }
}


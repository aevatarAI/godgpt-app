using System;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions.CQRS;
using Aevatar.App.Controllers;
using Microsoft.Extensions.Logging;

namespace Aevatar.App.HttpApi.Host.Services;

/// <summary>
/// State query service implementation using IStateIndexService.
/// Bridges the HTTP API layer with the Elasticsearch index service.
/// </summary>
public class StateQueryService : IStateQueryService
{
    private readonly IStateIndexService _indexService;
    private readonly ILogger<StateQueryService> _logger;

    public StateQueryService(
        IStateIndexService indexService,
        ILogger<StateQueryService> logger)
    {
        _indexService = indexService;
        _logger = logger;
    }

    public async Task<StateQueryResponseDto?> GetByIdAsync(string agentType, string agentId)
    {
        try
        {
            var result = await _indexService.GetByIdAsync(agentType, agentId);
            if (result == null)
            {
                return null;
            }

            return new StateQueryResponseDto
            {
                AgentId = result.AgentId,
                AgentType = result.AgentType,
                Data = result.Data,
                Version = result.Version
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying state: {Type}/{Id}", agentType, agentId);
            throw;
        }
    }

    public async Task<PagedStateQueryResponseDto> QueryAsync(StateQueryRequestDto request)
    {
        try
        {
            var query = new StateQuery
            {
                AgentType = request.AgentType,
                QueryString = request.QueryString,
                PageIndex = request.PageIndex,
                PageSize = request.PageSize,
                SortFields = request.SortFields
            };

            var result = await _indexService.QueryAsync(query);

            return new PagedStateQueryResponseDto
            {
                TotalCount = result.TotalCount,
                Items = result.Items.Select(i => new StateQueryResponseDto
                {
                    AgentId = i.AgentId,
                    AgentType = i.AgentType,
                    Data = i.Data,
                    Version = i.Version
                }).ToList(),
                PageIndex = result.PageIndex,
                PageSize = result.PageSize,
                TotalPages = result.TotalPages
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying states: {Type}", request.AgentType);
            throw;
        }
    }

    public async Task<long> CountAsync(string agentType, string? queryString)
    {
        try
        {
            return await _indexService.CountAsync(agentType, queryString);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error counting states: {Type}", agentType);
            throw;
        }
    }
}


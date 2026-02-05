using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.App.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Volo.Abp.AspNetCore.Mvc;

namespace Aevatar.App.Controllers;

/// <summary>
/// State Query Controller for CQRS read model
/// Provides endpoints to query agent states from Elasticsearch
/// </summary>
[Route("api/states")]
[ApiController]
public class StateQueryController : AbpControllerBase
{
    private readonly IStateQueryService _stateQueryService;
    private readonly ILogger<StateQueryController> _logger;

    public StateQueryController(
        IStateQueryService stateQueryService,
        ILogger<StateQueryController> logger)
    {
        _stateQueryService = stateQueryService;
        _logger = logger;
    }

    /// <summary>
    /// Get state by agent ID
    /// </summary>
    /// <param name="agentType">Agent type name</param>
    /// <param name="agentId">Agent ID</param>
    /// <returns>Agent state</returns>
    [HttpGet("{agentType}/{agentId}")]
    [Authorize(Policy = AppPermissions.CqrsManagement.States)]
    public async Task<ActionResult<StateQueryResponseDto>> GetById(
        [FromRoute] string agentType,
        [FromRoute] string agentId)
    {
        _logger.LogInformation("📊 Querying state for agent {AgentType}/{AgentId}", agentType, agentId);

        try
        {
            var result = await _stateQueryService.GetByIdAsync(agentType, agentId);
            if (result == null)
            {
                return NotFound($"State not found for agent {agentType}/{agentId}");
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying state for agent {AgentType}/{AgentId}", agentType, agentId);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Query states with Lucene query syntax
    /// </summary>
    /// <param name="request">Query request</param>
    /// <returns>Paged state results</returns>
    [HttpPost("query")]
    [Authorize(Policy = AppPermissions.CqrsManagement.States)]
    public async Task<ActionResult<PagedStateQueryResponseDto>> Query([FromBody] StateQueryRequestDto request)
    {
        _logger.LogInformation(
            "📊 Querying states for type {AgentType}, query: {Query}, page: {Page}",
            request.AgentType, request.QueryString ?? "*", request.PageIndex);

        try
        {
            var result = await _stateQueryService.QueryAsync(request);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying states for type {AgentType}", request.AgentType);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Count states matching query
    /// </summary>
    /// <param name="agentType">Agent type name</param>
    /// <param name="queryString">Optional Lucene query string</param>
    /// <returns>Count of matching states</returns>
    [HttpGet("{agentType}/count")]
    [Authorize(Policy = AppPermissions.CqrsManagement.States)]
    public async Task<ActionResult<StateCountResponseDto>> Count(
        [FromRoute] string agentType,
        [FromQuery] string? queryString = null)
    {
        _logger.LogInformation("📊 Counting states for type {AgentType}, query: {Query}",
            agentType, queryString ?? "*");

        try
        {
            var count = await _stateQueryService.CountAsync(agentType, queryString);
            return Ok(new StateCountResponseDto { AgentType = agentType, Count = count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error counting states for type {AgentType}", agentType);
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

// ========== DTOs ==========

/// <summary>
/// State query request
/// </summary>
public class StateQueryRequestDto
{
    /// <summary>
    /// Agent type (index name)
    /// </summary>
    public string AgentType { get; set; } = string.Empty;

    /// <summary>
    /// Lucene query string (optional)
    /// Examples: "status:active", "version:>10", "name:John*"
    /// </summary>
    public string? QueryString { get; set; }

    /// <summary>
    /// Page index (0-based)
    /// </summary>
    public int PageIndex { get; set; }

    /// <summary>
    /// Page size (default: 20)
    /// </summary>
    public int PageSize { get; set; } = 20;

    /// <summary>
    /// Sort fields (format: "field:asc" or "field:desc")
    /// </summary>
    public List<string> SortFields { get; set; } = new();
}

/// <summary>
/// Single state query response
/// </summary>
public class StateQueryResponseDto
{
    /// <summary>
    /// Agent ID
    /// </summary>
    public string AgentId { get; set; } = string.Empty;

    /// <summary>
    /// Agent type
    /// </summary>
    public string AgentType { get; set; } = string.Empty;

    /// <summary>
    /// State data
    /// </summary>
    public Dictionary<string, object?> Data { get; set; } = new();

    /// <summary>
    /// Version
    /// </summary>
    public long Version { get; set; }
}

/// <summary>
/// Paged state query response
/// </summary>
public class PagedStateQueryResponseDto
{
    /// <summary>
    /// Total count of matching documents
    /// </summary>
    public long TotalCount { get; set; }

    /// <summary>
    /// Result items
    /// </summary>
    public List<StateQueryResponseDto> Items { get; set; } = new();

    /// <summary>
    /// Current page index
    /// </summary>
    public int PageIndex { get; set; }

    /// <summary>
    /// Page size
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Total pages
    /// </summary>
    public int TotalPages { get; set; }
}

/// <summary>
/// State count response
/// </summary>
public class StateCountResponseDto
{
    /// <summary>
    /// Agent type
    /// </summary>
    public string AgentType { get; set; } = string.Empty;

    /// <summary>
    /// Count of matching states
    /// </summary>
    public long Count { get; set; }
}

// ========== Service Interface ==========

/// <summary>
/// State query service interface (to be implemented by Silo)
/// </summary>
public interface IStateQueryService
{
    Task<StateQueryResponseDto?> GetByIdAsync(string agentType, string agentId);
    Task<PagedStateQueryResponseDto> QueryAsync(StateQueryRequestDto request);
    Task<long> CountAsync(string agentType, string? queryString);
}


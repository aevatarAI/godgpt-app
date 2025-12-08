using System.Text.Json;
using Aevatar.CognitiveMesh.Dsl.Models;
using Aevatar.CognitiveMesh.Dsl.Validation;

namespace Aevatar.CognitiveMesh.Dsl.Tests;

public class CognitiveDslCompilerTests
{
    private readonly CognitiveDslCompiler _compiler = new();

    [Fact]
    public void Compile_WithValidPayload_ReturnsNormalizedDefinition()
    {
        var definition = _compiler.Compile(ValidDsl);

        Assert.Equal("0.1", definition.DslVersion);
        Assert.Equal(StrategyKind.UotExpl, definition.Strategy);
        Assert.Equal(2, definition.Nodes.Count);
        Assert.Equal("explorer", definition.Nodes[0].Id);

        Assert.True(definition.Nodes[0].Params.TryGetValue("branch_width", out var width));
        Assert.Equal(5, width.GetInt32());

        Assert.Single(definition.Edges);
        Assert.Single(definition.Constraints);
    }

    [Fact]
    public void Compile_WithDuplicateNodeIds_ThrowsCompilationException()
    {
        var ex = Assert.Throws<DslCompilationException>(() => _compiler.Compile(DuplicateNodeDsl));

        Assert.Contains(ex.Errors, error => error.Code == "node.duplicate_id");
    }

    [Fact]
    public void Compile_TransformativeStrategyWithoutMetaAgent_Throws()
    {
        var ex = Assert.Throws<DslCompilationException>(() => _compiler.Compile(TransformativeWithoutMetaDsl));

        Assert.Contains(ex.Errors, error => error.Code == "strategy.meta_agent_missing");
    }

    private const string ValidDsl = """
    {
      "dsl_version": "0.1",
      "goal": {
        "name": "Discover new catalysts",
        "success_metric": "simulation_score"
      },
      "strategy": "uot_expl",
      "budget": {
        "max_steps": 200,
        "token_limit": 200000
      },
      "nodes": [
        {
          "id": "explorer",
          "type": "DivergentAgent",
          "params": {
            "branch_width": 5
          }
        },
        {
          "id": "judge",
          "type": "ConvergentAgent"
        }
      ],
      "edges": [
        {
          "from": "explorer",
          "to": "judge",
          "channel": "submit_hypothesis"
        }
      ],
      "constraints": [
        {
          "type": "confidence_threshold",
          "value": 0.9
        }
      ]
    }
    """;

    private const string DuplicateNodeDsl = """
    {
      "dsl_version": "0.1",
      "goal": { "name": "Check duplicates" },
      "strategy": "cot",
      "budget": { "max_steps": 10, "token_limit": 1000 },
      "nodes": [
        { "id": "dup", "type": "DivergentAgent" },
        { "id": "dup", "type": "CriticAgent" }
      ],
      "edges": [],
      "constraints": []
    }
    """;

    private const string TransformativeWithoutMetaDsl = """
    {
      "dsl_version": "0.1",
      "goal": { "name": "Transform without meta" },
      "strategy": "uot_trans",
      "budget": { "max_steps": 20, "token_limit": 2000 },
      "nodes": [
        { "id": "worker", "type": "DivergentAgent" }
      ],
      "edges": [],
      "constraints": []
    }
    """;
}

using Aevatar.Agents.Cognitive.Engine;
using Aevatar.Agents.Cognitive.Primitives;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Cognitive.Tests;

public class WorkflowParserTests
{
    private static string RepoRoot()
    {
        // ============================================================
        //  Repo root discovery (test runs from bin/)
        //  - Find the nearest Directory.Packages.props as the monorepo anchor.
        // ============================================================
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Directory.Packages.props")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found (Directory.Packages.props missing).");
    }

    [Fact]
    public void Parse_ShouldLoadHypothesisPromotionLoopWorkflow()
    {
        var parser = new WorkflowParser();
        var wf = parser.ParseFile(Path.Combine(
            RepoRoot(),
            "src",
            "Aevatar.Agents.Cognitive",
            "workflows",
            "hypothesis_promotion_loop.yaml"));

        wf.Name.ShouldBe("hypothesis_promotion_loop");
        wf.Steps.Count.ShouldBeGreaterThan(0);

        var scout = wf.Steps.Single(s => s.Id == "refute_scout");
        scout.Type.ShouldBe("fan_out");
        scout.Parameters.ContainsKey("include_failures").ShouldBeTrue();

        var child = scout.Step;
        child.ShouldNotBeNull();
        child!.Type.ShouldBe("llm_call");

        // defaults.llm_call should be injected
        child.Parameters.ContainsKey("timeout_seconds").ShouldBeTrue();
        child.Parameters.ContainsKey("idle_timeout_seconds").ShouldBeTrue();
        child.Parameters.ContainsKey("max_length").ShouldBeTrue();
        child.Parameters.ContainsKey("strict_parse").ShouldBeTrue();
    }

    [Fact]
    public void Parse_ShouldLoadHypothesisPromotionLoopHpaWorkflow()
    {
        var parser = new WorkflowParser();
        var wf = parser.ParseFile(Path.Combine(
            RepoRoot(),
            "src",
            "Aevatar.Agents.Cognitive",
            "workflows",
            "hypothesis_promotion_loop_hpa.yaml"));

        wf.Name.ShouldBe("hypothesis_promotion_loop_hpa");
        wf.Steps.Count.ShouldBeGreaterThan(0);

        var hpa = wf.Steps.Single(s => s.Id == "hpa_prepare");
        hpa.Type.ShouldBe("hpa");

        hpa.Parameters.ContainsKey("ops").ShouldBeTrue();
    }

    [Fact]
    public void Parse_ShouldSupportDefaultsInMakerV2()
    {
        var parser = new WorkflowParser();
        var wf = parser.ParseFile(Path.Combine(
            RepoRoot(),
            "src",
            "Aevatar.Agents.Cognitive",
            "workflows",
            "maker-v2.yaml"));

        wf.Name.ShouldBe("maker-v2");
        wf.Steps.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Parse_ShouldNormalizeNestedMappings_ForTransformOps()
    {
        var yaml = @"
name: t
version: '1.0'
steps:
  - id: agg
    type: transform
    ops:
      - op: count_where
        from: xs
        where: { field: 'accept', equals: true }
        store: n
output:
  n: '{{n}}'
";

        var parser = new WorkflowParser();
        var wf = parser.Parse(yaml);

        var step = wf.Steps.Single(s => s.Id == "agg");
        step.Type.ShouldBe("transform");

        step.Parameters.TryGetValue("ops", out var opsObj).ShouldBeTrue();
        opsObj.ShouldBeOfType<List<Dictionary<string, object?>>>();

        var ops = (List<Dictionary<string, object?>>)opsObj!;
        ops.Count.ShouldBe(1);

        var op0 = ops[0];
        op0.ContainsKey("where").ShouldBeTrue();
        op0["where"].ShouldBeOfType<Dictionary<string, object?>>();
    }
}

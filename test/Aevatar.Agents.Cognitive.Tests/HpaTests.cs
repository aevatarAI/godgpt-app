using Aevatar.Agents.Cognitive.Execution;
using Aevatar.Agents.Cognitive.Hpa;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.Cognitive.Template;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Cognitive.Tests;

public class HpaTests
{
    // ============================================================
    //  Octonion helpers
    // ============================================================

    private static Octonion Basis(int idx)
    {
        // idx: 0..7 => 1, e1..e7
        return idx switch
        {
            0 => Octonion.One,
            1 => new Octonion(0, 1, 0, 0, 0, 0, 0, 0),
            2 => new Octonion(0, 0, 1, 0, 0, 0, 0, 0),
            3 => new Octonion(0, 0, 0, 1, 0, 0, 0, 0),
            4 => new Octonion(0, 0, 0, 0, 1, 0, 0, 0),
            5 => new Octonion(0, 0, 0, 0, 0, 1, 0, 0),
            6 => new Octonion(0, 0, 0, 0, 0, 0, 1, 0),
            7 => new Octonion(0, 0, 0, 0, 0, 0, 0, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(idx))
        };
    }

    private static void ShouldBeBasis(Octonion x, int idx, double sign = 1.0)
    {
        var a = x.ToArray();
        for (var i = 0; i < 8; i++)
        {
            var expected = (i == idx) ? sign : 0.0;
            a[i].ShouldBe(expected);
        }
    }

    [Fact]
    public void Octonion_ImaginarySquares_ShouldBeMinusOne()
    {
        for (var i = 1; i <= 7; i++)
        {
            var e = Basis(i);
            var p = e * e;
            ShouldBeBasis(p, 0, -1.0);
        }
    }

    [Fact]
    public void Octonion_FanoCycle_ShouldRespectOrientation()
    {
        // (1,2,3): e1*e2 = e3 and e2*e1 = -e3
        var e1 = Basis(1);
        var e2 = Basis(2);
        var e3 = Basis(3);

        ShouldBeBasis(e1 * e2, 3, +1.0);
        ShouldBeBasis(e2 * e1, 3, -1.0);

        // sanity: e1*e2 != e2*e1
        (e1 * e2).ToArray().ShouldNotBe((e2 * e1).ToArray());
        e3.Norm().ShouldBe(1.0);
    }

    [Fact]
    public void Octonion_Associator_ShouldBeNonZero_ForNonQuaternionicTriple()
    {
        // Pick a triple that is not contained in a single Fano line (non-quaternionic).
        var e1 = Basis(1);
        var e2 = Basis(2);
        var e4 = Basis(4);

        var a = Octonion.Associator(e1, e2, e4);
        a.Norm().ShouldBeGreaterThan(0.0);
    }

    // ============================================================
    //  HpaExecutor ops
    // ============================================================

    [Fact]
    public void HpaExecutor_ScanTarget_ShouldComputePhase()
    {
        var vars = new Dictionary<string, object>();
        var ops = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["op"] = "scan_target",
                ["iteration"] = 3,
                ["alpha"] = 0.5,
                ["seed_phase"] = 0.1,
                ["store"] = "scan"
            }
        };

        var step = new StepDefinition
        {
            Id = "scan",
            Type = "hpa",
            Parameters = new Dictionary<string, object?> { ["ops"] = ops }
        };

        var ex = new HpaExecutor(new TemplateEngine(), NullLogger.Instance);
        var r = ex.Execute(step, vars);
        r.Success.ShouldBeTrue(r.Error);

        var scan = (Dictionary<string, object>)vars["scan"];
        ((double)scan["target_phase01"]).ShouldBe(0.6, 1e-12);
        ((double)scan["target_phase"]).ShouldBe(2.0 * Math.PI * 0.6, 1e-12);
        ((int)scan["scan_k"]).ShouldBe(3);
    }

    [Fact]
    public void HpaExecutor_EmbedNode_ShouldAttachEmbedding_AndSupportPurePathTemplate()
    {
        var candidate = new Dictionary<string, object>
        {
            ["id"] = "H1",
            ["statement"] = "S",
            ["depends_on"] = new List<object> { "O1", "T2" },
            ["factor_sequence"] = new List<object> { "O1", "T2" }
        };

        var vars = new Dictionary<string, object>
        {
            ["candidate"] = candidate
        };

        var ops = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["op"] = "embed_node",
                ["node"] = "{{ candidate }}",
                ["attach_field"] = "hpa",
                ["store"] = "cand_embed"
            }
        };

        var step = new StepDefinition
        {
            Id = "embed",
            Type = "hpa",
            Parameters = new Dictionary<string, object?> { ["ops"] = ops }
        };

        var ex = new HpaExecutor(new TemplateEngine(), NullLogger.Instance);
        var r = ex.Execute(step, vars);
        r.Success.ShouldBeTrue(r.Error);

        candidate.ContainsKey("hpa").ShouldBeTrue();
        vars.ContainsKey("cand_embed").ShouldBeTrue();

        var embed = (Dictionary<string, object>)vars["cand_embed"];
        embed.ContainsKey("rho").ShouldBeTrue();
        embed.ContainsKey("theta").ShouldBeTrue();
        embed.ContainsKey("z_re").ShouldBeTrue();
        embed.ContainsKey("z_im").ShouldBeTrue();
        embed.ContainsKey("u_oct").ShouldBeTrue();
        embed.ContainsKey("factors").ShouldBeTrue();
    }

    [Fact]
    public void HpaExecutor_EvidenceSynthesize_ShouldProjectToNearestTheorem_WhenAligned()
    {
        // --------------------------------------------------------
        //  Arrange: hand-inject embeddings to make the math deterministic:
        //  candidate z = (1,0), two accept verdicts with unit (1,0)
        //  => v = candidate * (2,0) = (2,0)
        //  Provide theorem lattice point T1 at (2,0) => gap = 0.
        // --------------------------------------------------------
        var candidate = new Dictionary<string, object>
        {
            ["id"] = "H1",
            ["statement"] = "S",
            ["hpa"] = new Dictionary<string, object> { ["z_re"] = 1.0, ["z_im"] = 0.0 }
        };

        var verdicts = new List<object>
        {
            new Dictionary<string, object>
            {
                ["worker_id"] = "w0",
                ["accept"] = true,
                ["strong_refutation"] = false,
                ["confidence"] = 1.0,
                ["hpa"] = new Dictionary<string, object> { ["z_re"] = 1.0, ["z_im"] = 0.0 }
            },
            new Dictionary<string, object>
            {
                ["worker_id"] = "w1",
                ["accept"] = true,
                ["strong_refutation"] = false,
                ["confidence"] = 1.0,
                ["hpa"] = new Dictionary<string, object> { ["z_re"] = 1.0, ["z_im"] = 0.0 }
            }
        };

        var theoremIndex = new Dictionary<string, object>
        {
            ["T1"] = new Dictionary<string, object> { ["z_re"] = 2.0, ["z_im"] = 0.0 }
        };

        var relevantFacts = new List<object>
        {
            new Dictionary<string, object> { ["id"] = "T1", ["score"] = 1.0 }
        };

        var vars = new Dictionary<string, object>
        {
            ["candidate"] = candidate,
            ["worker_verdicts"] = verdicts,
            ["theorem_index"] = theoremIndex,
            ["relevant_facts"] = relevantFacts
        };

        var ops = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["op"] = "evidence_synthesize",
                ["candidate"] = "{{ candidate }}",
                ["verdicts"] = "worker_verdicts",
                ["relevant"] = "relevant_facts",
                ["theorem_index"] = "theorem_index",
                ["store"] = "evidence"
            }
        };

        var step = new StepDefinition
        {
            Id = "evidence",
            Type = "hpa",
            Parameters = new Dictionary<string, object?> { ["ops"] = ops }
        };

        var ex = new HpaExecutor(new TemplateEngine(), NullLogger.Instance);
        var r = ex.Execute(step, vars);
        r.Success.ShouldBeTrue(r.Error);

        var evidence = (Dictionary<string, object>)vars["evidence"];
        ((int)evidence["accept_count"]).ShouldBe(2);
        ((int)evidence["strong_refutation_count"]).ShouldBe(0);
        ((double)evidence["coherence"]).ShouldBeGreaterThan(0.99);
        evidence["projection_id"].ToString().ShouldBe("T1");
        ((double)evidence["gap_norm"]).ShouldBe(0.0);
    }

    [Fact]
    public void HpaExecutor_AssociatorStats_ShouldCountTriples_FromSequences()
    {
        var candidate = new Dictionary<string, object>
        {
            ["factor_sequence"] = new List<object> { "A", "B", "C" } // 1 triple
        };

        var verdicts = new List<object>
        {
            new Dictionary<string, object>
            {
                ["accept"] = true,
                ["factor_sequence"] = new List<object> { "D", "E", "F", "G" } // 2 triples
            }
        };

        var vars = new Dictionary<string, object>
        {
            ["candidate"] = candidate,
            ["worker_verdicts"] = verdicts
        };

        var ops = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["op"] = "associator_stats",
                ["candidate"] = "{{ candidate }}",
                ["verdicts"] = "worker_verdicts",
                ["store"] = "assoc"
            }
        };

        var step = new StepDefinition
        {
            Id = "assoc",
            Type = "hpa",
            Parameters = new Dictionary<string, object?> { ["ops"] = ops }
        };

        var ex = new HpaExecutor(new TemplateEngine(), NullLogger.Instance);
        var r = ex.Execute(step, vars);
        r.Success.ShouldBeTrue(r.Error);

        var assoc = (Dictionary<string, object>)vars["assoc"];
        ((int)assoc["triple_count"]).ShouldBe(3);
        ((double)assoc["associator_mean"]).ShouldBeGreaterThanOrEqualTo(0.0);
        ((double)assoc["associator_max"]).ShouldBeGreaterThanOrEqualTo(0.0);
    }

    [Fact]
    public void HpaExecutor_GapHolonomy_ShouldSumGapVectors()
    {
        var gaps = new List<object>
        {
            new Dictionary<string, object> { ["z_re"] = 0.5, ["z_im"] = 0.25 },
            new Dictionary<string, object> { ["z_re"] = -0.1, ["z_im"] = 0.75 }
        };

        var vars = new Dictionary<string, object>
        {
            ["edge_gaps"] = gaps
        };

        var ops = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["op"] = "gap_holonomy",
                ["gaps"] = "edge_gaps",
                ["store"] = "hol"
            }
        };

        var step = new StepDefinition
        {
            Id = "hol",
            Type = "hpa",
            Parameters = new Dictionary<string, object?> { ["ops"] = ops }
        };

        var ex = new HpaExecutor(new TemplateEngine(), NullLogger.Instance);
        var r = ex.Execute(step, vars);
        r.Success.ShouldBeTrue(r.Error);

        var hol = (Dictionary<string, object>)vars["hol"];
        ((double)hol["holonomy_re"]).ShouldBe(0.4, 1e-12);
        ((double)hol["holonomy_im"]).ShouldBe(1.0, 1e-12);
        ((int)hol["edge_count"]).ShouldBe(2);
    }
}


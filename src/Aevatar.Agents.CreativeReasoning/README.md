# Aevatar.Agents.CreativeReasoning

Universe of Thoughts (UoT) implementation for creative reasoning with LLMs.

Based on the research paper: [Universe of Thoughts: Enabling Creative Reasoning with Large Language Models](https://arxiv.org/html/2511.20471v2)

## Overview

This module implements **three levels of creative reasoning**:

| Mode | Creativity Level | Description |
|------|-----------------|-------------|
| **C-UoT** | Incremental | Combine existing thoughts from analogous problems |
| **E-UoT** | Exploratory | Discover outside thoughts beyond known solutions |
| **T-UoT** | Transformative | Challenge rules and hidden assumptions |

### When to Use Which Mode?

```
Problem Type                    → Recommended Mode
─────────────────────────────────────────────────
Optimize existing approach      → C-UoT
Need fresh perspectives         → E-UoT  
"Impossible" constraints        → T-UoT
Disruptive innovation needed    → T-UoT
```

## Quick Start

```csharp
// Register services
services.AddUoTCreativeReasoning();

var executor = serviceProvider.GetRequiredService<IUoTExecutor>();

// C-UoT: Combinational (default)
var result = await executor.ExecuteAsync(
    "Design a traffic management system for a single-lane bridge",
    new UoTOptions
    {
        Mode = UoTMode.Combinational,
        ProviderName = "openai-gpt4",
        DomainHint = "transportation, distributed systems"
    });

// E-UoT: Exploratory
var result = await executor.ExecuteAsync(
    "Create innovative features for a fitness app",
    new UoTOptions
    {
        Mode = UoTMode.Exploratory,
        ProviderName = "openai-gpt4",
        MaxOutsideThoughts = 15,
        ExplorationDirections = 4
    });

// T-UoT: Transformative (with specialized result)
var tuotResult = await executor.ExecuteTransformativeAsync(
    "How can traditional bookstores compete with e-commerce?",
    new UoTOptions
    {
        Mode = UoTMode.Transformative,
        ProviderName = "openai-gpt4",
        MaxRuleSets = 3,
        MinRadicality = 0.6f
    });

// T-UoT reveals hidden assumptions
foreach (var assumption in tuotResult.Trace.HiddenAssumptions)
{
    Console.WriteLine($"Hidden assumption: {assumption.Content}");
}
```

## C-UoT: Combinational Creative Reasoning

**Six-step process:**

1. **Analogical Retrieval**: Find similar problems from diverse domains
2. **Decompose into Thoughts**: Break solutions into atomic units
3. **Select Host**: Choose best base solution
4. **Select Donors**: Far-then-Analogical selection for novelty
5. **Synthesize**: Combine host + donor thoughts
6. **Evaluate**: Feasibility (gate) → Utility + Novelty

```
Problem → Find Analogies → Decompose → Select Host → Select Donors → Synthesize → Evaluate
            ↓                ↓              ↓              ↓             ↓           ↓
    [Different domains] [Atomic thoughts] [Best base] [Novel donors] [New combo]  [Score]
```

## E-UoT: Exploratory Creative Reasoning

**Extends C-UoT with Step E:**

```
C-UoT Steps 1-2 → Step E: Explore Outside Thoughts → C-UoT Steps 3-6
                         ↓
            ┌────────────────────────────┐
            │ 1. Identify exploration    │
            │    directions              │
            │ 2. Discover outside        │
            │    thoughts                │
            │ 3. Evaluate novelty +      │
            │    relevance               │
            └────────────────────────────┘
```

**Key insight**: Don't just recombine existing thoughts—actively explore the universe for new conceptual primitives.

## T-UoT: Transformative Creative Reasoning

**The most profound form of creativity:**

```
Step 1: Expose Rules           → Explicit constraints + HIDDEN ASSUMPTIONS
             ↓
Step 2: Mutate Rules           → Create alternative rule sets
             ↓
Step 3: Explore Rule Spaces    → Discover previously impossible solutions
             ↓
Step 4: Evaluate               → Feasibility + Utility + Novelty + Radicality
```

**Philosophy**: Hidden assumptions are invisible cages. True innovation requires:
1. Making assumptions visible
2. Systematically challenging them
3. Exploring the resulting new solution spaces

### Rule Mutation Types

| Type | Description | Example |
|------|-------------|---------|
| NEGATE | Reverse entirely | "Must be physical" → "Can be virtual" |
| WEAKEN | Less strict | "24/7 availability" → "Peak hours only" |
| GENERALIZE | Broaden scope | "Books only" → "Any media" |
| SPECIALIZE | Narrow focus | "All users" → "Power users only" |
| REMOVE | Eliminate rule | Remove profitability constraint |
| COMBINE | Merge rules | Combine customer segments |

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                      UoTExecutor                             │
│    (Unified entry point, routes by UoTOptions.Mode)          │
└─────────────────────────────────────────────────────────────┘
                              │
         ┌────────────────────┼────────────────────┐
         ▼                    ▼                    ▼
┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐
│ UoTCoordinator  │  │ EUoTCoordinator │  │ TUoTCoordinator │
│    GAgent       │  │    GAgent       │  │    GAgent       │
│   (C-UoT)       │  │   (E-UoT)       │  │   (T-UoT)       │
└─────────────────┘  └─────────────────┘  └─────────────────┘
         │                    │                    │
         ▼                    ▼                    ▼
┌─────────────────────────────────────────────────────────────┐
│                      Strategies (Pluggable)                  │
│ - IAnalogyStrategy           - IExploratoryStrategy         │
│ - IThoughtDecompositionStrategy  - IRuleMutationStrategy    │
│ - IHostSelectionStrategy     - IDonorSelectionStrategy      │
│ - ISynthesisStrategy         - IEvaluationStrategy          │
└─────────────────────────────────────────────────────────────┘
```

## Evaluation Dimensions

| Dimension | Role | Description |
|-----------|------|-------------|
| **Feasibility** | Hard Constraint | Can this be implemented? (Must pass threshold) |
| **Utility** | Soft Metric | How effective is this solution? |
| **Novelty** | Soft Metric | How different from existing solutions? |
| **Radicality** | T-UoT Only | How transformative is this? |

**Composite Score**:
- C-UoT/E-UoT: `Utility × UtilityWeight + Novelty × NoveltyWeight`
- T-UoT: Adds radicality bonus (20%)

## Configuration Options

### Common Options

```csharp
new UoTOptions
{
    Mode = UoTMode.Exploratory,      // C/E/T mode
    ProviderName = "openai-gpt4",    // LLM provider
    DomainHint = "healthcare, AI",   // Domain context
    FeasibilityThreshold = 0.6f,     // Hard constraint
    UtilityWeight = 0.5f,
    NoveltyWeight = 0.5f,
    OnProgress = p => Console.WriteLine($"[{p.Phase}] {p.Message}")
}
```

### C-UoT Specific

```csharp
MaxAnalogies = 5,           // Analogous problems to find
SolutionsPerAnalogy = 3,    // Solutions per analogy
MaxCandidates = 10,         // Candidates to synthesize
FarDistanceThreshold = 0.6f // For donor selection
```

### E-UoT Specific

```csharp
MaxOutsideThoughts = 10,        // Outside thoughts to discover
ExplorationDirections = 3,      // Exploration directions
OutsideThoughtRelevance = 0.4f  // Min relevance threshold
```

### T-UoT Specific

```csharp
MaxRuleSets = 3,                   // Rule sets to explore
MutationsPerSet = 3,               // Mutations per set
MinRadicality = 0.5f,              // Min radicality threshold
AllowPhysicalRuleViolation = false // Usually false
```

## Example Use Cases

### Business Strategy (T-UoT)
```csharp
// Challenge hidden assumptions about retail
var result = await executor.ExecuteTransformativeAsync(
    "How can local newspapers survive?",
    new UoTOptions { Mode = UoTMode.Transformative, MinRadicality = 0.7f });

// Might expose hidden assumption: "Revenue must come from readers"
// Mutated rule: "Value flows from community connections"
// Transformative solution: "Community platform with journalism as service"
```

### Product Innovation (E-UoT)
```csharp
// Explore outside thoughts for fresh ideas
var result = await executor.ExecuteAsync(
    "Design fitness features for senior users",
    new UoTOptions { Mode = UoTMode.Exploratory, ExplorationDirections = 5 });

// Explores: geriatric medicine, social psychology, game design...
// Discovers outside thought: "Social accountability from team sports"
// Novel solution: "Virtual walking buddy with real-time conversation"
```

### System Design (C-UoT)
```csharp
// Combine proven patterns from different domains
var result = await executor.ExecuteAsync(
    "Design a traffic system for single-lane bridge",
    new UoTOptions { Mode = UoTMode.Combinational, MaxAnalogies = 7 });

// Finds analogies: network protocols, biological systems, auction mechanisms
// Combines: TCP flow control + ant colony optimization
```

## Custom Strategies

```csharp
// Inject custom strategies
coordinator.SetStrategies(
    analogyStrategy: new DomainSpecificAnalogyStrategy(),
    ruleMutationStrategy: new ConservativeRuleMutationStrategy(),
    evaluationStrategy: new IndustryCompliantEvaluationStrategy()
);
```

## References

- [Universe of Thoughts Paper (arXiv)](https://arxiv.org/html/2511.20471v2)
- [Margaret Boden's Three Types of Creativity](https://en.wikipedia.org/wiki/Computational_creativity)
  - Combinational: Novel combinations of familiar ideas
  - Exploratory: Exploring structured conceptual spaces
  - Transformational: Altering the rules of the space itself

## License

MIT License - See repository root for details.

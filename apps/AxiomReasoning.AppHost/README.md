# AxiomReasoning.AppHost

.NET Aspire AppHost for **Aevatar.AxiomReasoning** (multi-agent theorem discovery loop).

## 🚀 Quick Start

```bash
cd apps/AxiomReasoning.AppHost
dotnet run
```

## 📋 Services

| Service | Port | Description |
|--------|------|-------------|
| Axiom Reasoning | 5003 | Web UI + SSE progress for theorem discovery loop |

## 🌐 Endpoints

- **Web UI**: `http://localhost:5003`
- **API**: `http://localhost:5003/api/sessions`

## 🔗 Related

- `cognitive-mesh/Aevatar.AxiomReasoning/README.md`
- `src/Aevatar.Agents.Cognitive/workflows/axiom_theorem_loop.yaml`

# AxiomReasoning.AppHost

.NET Aspire AppHost for Axiom Reasoning - 多智能体公理推理平台（Cognitive Mesh + `axiom_reasoning.yaml`）。

## 🚀 Quick Start

```bash
# Navigate to AppHost directory
cd apps/AxiomReasoning.AppHost

# Run with Aspire
dotnet run
```

Aspire Dashboard will open automatically, providing:
- 📊 Distributed Tracing
- 📝 Structured Logs
- ❤️ Health Checks
- 📈 Metrics

## 📋 Services

| Service | Port | Description |
|---------|------|-------------|
| Axiom Reasoning | 5001 | Axiom/Theorem reasoning with web UI + API |

## 🌐 Endpoints

After starting:

- **Web UI**: http://localhost:5001
- **Health**: http://localhost:5001/health
- **API**: http://localhost:5001/api/sessions
- **SSE**: http://localhost:5001/api/sessions/{sessionId}/events

## ⚙️ Configuration

LLM providers are configured via `appsettings.secrets.json` in the service project:

- `cognitive-mesh/Aevatar.AxiomReasoning/appsettings.secrets.json`

## 🔗 Related

- [Axiom Reasoning Architecture](./docs/ARCHITECTURE.md)
- [Cognitive Mesh](../../cognitive-mesh/README.md)



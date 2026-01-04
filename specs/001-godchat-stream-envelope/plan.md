# Implementation Plan: Unified GodChat Stream Envelope (Text + Audio)

**Branch**: `001-godchat-stream-envelope` | **Date**: 2026-01-04 | **Spec**: `specs/001-godchat-stream-envelope/spec.md`  
**Input**: Feature specification from `specs/001-godchat-stream-envelope/spec.md`

## Summary

Unify the client-facing streaming contract into a single Protobuf envelope (`GodChatStreamEnvelopeProto`) so HttpApi consumes exactly one stream message type. Split production into:
- **Text**: `AIAgentStatusProxy` publishes aggregated `text` chunks immediately (best TTFT).
- **Audio**: a dedicated `VoiceSynthesisGAgent` generates and publishes `audio` chunks asynchronously, but writes back to the **same** `StreamId` + `"GodChat"` category so the consumer remains single-stream.

This eliminates the current risk where multiple `ResponseStreamGodChatProto` definitions produce mismatched TypeUrls and cause ChatMiddleware to silently drop messages.

## Technical Context

**Language/Version**: .NET 10  
**Primary Dependencies**: Orleans 9.2.1, MassTransit (Kafka), Google.Protobuf, ASP.NET Core SSE  
**Storage**: Existing persistence for chat history (unchanged by this feature)  
**Testing**: Existing integration script `scripts/test-ai-chat-flow.sh` (extend for voice streaming)  
**Target Platform**: macOS/Linux local dev; server deployment (unchanged)  
**Project Type**: Monorepo with apps/ (HttpApi, Silo) and agents/ (GodGPT agents)  
**Performance Goals**: Text TTFT p95 <= 2s; reduce message count by aggregation; audio should never block text  
**Constraints**: All cross-boundary types MUST be Protobuf (framework requirement); keep consumption to one stream subscription in HttpApi.

## Constitution Check

The constitution file is currently a placeholder template. We will follow repository rules:
- All stream messages and events that cross boundaries must be Protobuf.
- Avoid state/event types as handwritten C# classes where serialization is required.

## Project Structure

### Documentation (this feature)

```text
specs/001-godchat-stream-envelope/
├── spec.md
├── plan.md
├── tasks.md
└── quickstart.md                    # How to run local verification (optional but recommended)
```

### Source Code (repository root)

```text
agents/
└── Aevatar.Agents.GodGPT/
    ├── AIAgentStatusProxy/
    │   ├── AIAgentStatusProxy.cs
    │   └── Protos/
    ├── GodChat/
    │   ├── GodChatGAgent.*.cs
    │   └── GodChatConversions.cs
    └── (new) VoiceSynthesis/
        ├── VoiceSynthesisGAgent.cs
        └── Protos/ (if needed)

apps/
└── Aevatar.App/
    └── src/Aevatar.App.HttpApi.Host/Handler/ChatMiddleware.cs

scripts/
└── test-ai-chat-flow.sh
```

**Structure Decision**: Implement a new unified stream contract under `agents/Aevatar.Agents.GodGPT/Protos/` and migrate both producers and the single consumer (ChatMiddleware) to use it. Add a dedicated voice synthesis agent under GodGPT agents to keep text and audio responsibilities isolated while keeping consumption unified.

## Architecture Decisions

### AD-001: Single consumer stream contract (one TypeUrl)
- **Decision**: Introduce `GodChatStreamEnvelopeProto` with `oneof payload { text | audio | control }`.
- **Rationale**: Prevent silent drops due to multiple `ResponseStreamGodChatProto` definitions and simplify ChatMiddleware.

### AD-002: Seq ownership and ordering
- **Decision**: `AIAgentStatusProxy` owns `seq` for text chunks; audio chunks reference text by `text_seq_ref` + `sentence_index`.
- **Rationale**: Sentence-level alignment is stable and avoids token-level coupling; keeps consumer logic simple.

### AD-003: Dedicated voice synthesis agent
- **Decision**: Add `VoiceSynthesisGAgent` to perform TTS off the text path.
- **Rationale**: TTS is slow and variable; isolating it avoids degrading text TTFT and reduces complexity in `GodChatGAgent.Callbacks`.

### AD-004: Completion semantics
- **Decision**: Control messages distinguish `text_completed`, `audio_completed`, and `all_completed`.
- **Rationale**: Audio may lag; completion must be explicit to avoid hanging SSE or premature close.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| N/A | No constitution constraints defined | N/A |

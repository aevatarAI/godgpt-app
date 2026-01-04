# Feature Specification: Unified GodChat Stream Envelope (Text + Audio)

**Feature Branch**: `001-godchat-stream-envelope`  
**Created**: 2026-01-04  
**Status**: Draft  
**Input**: "One unified consumer stream; split processing into text and voice paths; remove duplicated ResponseStreamGodChatProto contracts; design for refactor stage (cleanest solution)."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Single-stream text delivery via unified envelope (Priority: P1)

As a user chatting over HTTP/SSE, I want the AI response to start streaming quickly, and I want all stream messages to be delivered through a single, stable Protobuf contract to avoid TypeUrl mismatches and silent drops.

**Why this priority**: This is the primary UX (TTFT) and the root of recent outages (duplicate `ResponseStreamGodChatProto` definitions causing `Payload.Is(...)` to fail).

**Independent Test**: Run `scripts/test-ai-chat-flow.sh` and verify SSE receives messages (no timeouts) and `TTFT <= 2s` for simple and long prompts.

**Acceptance Scenarios**:

1. **Given** an authenticated HTTP chat request, **When** LLM streaming starts, **Then** the first SSE message is received within 2 seconds and contains a `text` payload.
2. **Given** a long response, **When** streaming proceeds, **Then** the number of Kafka/MassTransit messages is reduced via aggregation (e.g., <= 10 per typical response) while maintaining smooth streaming.
3. **Given** the stream is completed, **When** the final chunk is sent, **Then** a `control.completed` payload is emitted and the SSE connection closes cleanly.

---

### User Story 2 - Voice chat: text first, audio async, still one stream to consume (Priority: P1)

As a user in voice chat mode, I want to see text immediately (same TTFT as text chat) and receive audio later without blocking text delivery, but still through the same single stream subscription.

**Why this priority**: Voice TTS is inherently slower and should not block TTFT; current architecture risks regressing voice chat when bypassing callbacks.

**Independent Test**: Trigger voice chat SSE and confirm:
- first `text` payload arrives quickly
- `audio` payloads arrive later on the same SSE stream
- stream still completes with a `control.completed`

**Acceptance Scenarios**:

1. **Given** a voice chat HTTP request, **When** the LLM produces tokens, **Then** the client receives `text` payloads immediately on SSE (TTFT <= 2s).
2. **Given** enough text for speech, **When** TTS finishes, **Then** the client receives an `audio` payload on the same SSE stream with a reference to the related text segment.
3. **Given** TTS fails, **When** audio generation errors, **Then** the client receives a `control.error` payload for audio while text streaming continues and completes.

---

### User Story 3 - Contract de-duplication and migration to a single stream contract (Priority: P2)

As a developer, I want exactly one Protobuf contract for client-facing stream output so there is no ambiguity between multiple `ResponseStreamGodChatProto` packages.

**Why this priority**: It prevents the exact class of bugs we hit (silent early return in middleware when `Payload.Is(...)` is false).

**Independent Test**: Build solution + run streaming tests; grep for `ResponseStreamGodChatProto` usage in streaming path; confirm ChatMiddleware only unpacks the new envelope message.

**Acceptance Scenarios**:

1. **Given** a streaming message arrives, **When** ChatMiddleware validates payload type, **Then** it matches exactly one expected TypeUrl and is always processed.
2. **Given** duplicate proto definitions exist, **When** the refactor is complete, **Then** the client-facing stream path no longer references them.

---

### Edge Cases

- **SSE reconnect**: client reconnects mid-stream; server should keep publishing chunks; client can reassemble by `(chat_id, seq)`.
- **Out-of-order arrival**: consumer may receive chunks out of order; client should sort by `seq`.
- **Audio backlog**: TTS slower than text; audio can lag without blocking text; audio completion is distinct from text completion.
- **Partial audio**: produce audio in sentence-sized chunks; avoid enormous payloads.
- **Provider errors**: LLM errors and TTS errors are independent; error payload should indicate which modality failed.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST define a single Protobuf message `GodChatStreamEnvelopeProto` for all client-facing streaming output.
- **FR-002**: System MUST wrap `GodChatStreamEnvelopeProto` in `EventEnvelope.Payload` (Any.Pack) when producing to MassTransit/Kafka streams.
- **FR-003**: ChatMiddleware MUST only unpack `GodChatStreamEnvelopeProto` from `EventEnvelope` and MUST NOT depend on any `ResponseStreamGodChatProto` variant for streaming output.
- **FR-004**: `AIAgentStatusProxy` MUST publish `text` payloads to the stream `GetStream(streamId, "GodChat")` and maintain aggregation for message count reduction.
- **FR-005**: A dedicated voice synthesis component (recommended: `VoiceSynthesisGAgent`) MUST publish `audio` payloads back to the same `streamId` and `category` so the consumer remains single-stream.
- **FR-006**: The envelope MUST include a monotonically increasing `seq` for deterministic ordering by clients.
- **FR-007**: The envelope MUST include `chat_id` and `stream_id` to support filtering and correlation.
- **FR-008**: The stream MUST include explicit completion control messages, and MUST distinguish `text_completed` from `audio_completed`.
- **FR-009**: All cross-boundary types (stream messages and events) MUST be Protobuf-defined.

### Key Entities *(include if feature involves data)*

- **GodChatStreamEnvelopeProto**: The single streaming contract (text/audio/control) published to Kafka/MassTransit stream.
- **VoiceSynthesisJobProto**: A Protobuf job request instructing the voice synthesis agent to generate audio for a text segment.
- **TextChunkProto**: A streaming text chunk, including content and optional references.
- **AudioChunkProto**: A streaming audio chunk, including audio bytes, metadata, and a reference to the related text segment.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Text TTFT p95 <= 2s on local test environment for both simple and long prompts.
- **SC-002**: Streaming responses no longer silently drop due to payload type mismatch; ChatMiddleware always logs first chunk and completes normally.
- **SC-003**: Kafka/MassTransit message count reduced by aggregation to <= 10 messages for typical responses (excluding audio).
- **SC-004**: Voice chat delivers text immediately (SC-001) and audio is delivered asynchronously with clear correlation, without blocking text streaming.

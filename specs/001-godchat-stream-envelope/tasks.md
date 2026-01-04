---
description: "Tasks for unified GodChat stream envelope (single consumer stream) and voice synthesis split"
---

# Tasks: Unified GodChat Stream Envelope (Text + Audio)

**Input**: `specs/001-godchat-stream-envelope/spec.md` + `specs/001-godchat-stream-envelope/plan.md`  
**Prerequisites**: spec.md, plan.md  
**Organization**: Grouped by user story to enable independent verification.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel
- **[Story]**: US1/US2/US3
- Each task includes concrete file paths.

---

## Phase 1: Foundations (Blocking)

- [ ] T001 [US1] Define unified stream contract protos in `agents/Aevatar.Agents.GodGPT/Protos/god_chat_stream.proto` (envelope + oneof payloads: text/audio/control)
- [ ] T002 [US2] Define voice synthesis job proto in `agents/Aevatar.Agents.GodGPT/Protos/voice_synthesis_job.proto` (job input references chat_id/stream_id + text refs)
- [ ] T003 [P] [US1] Add conversion helpers for envelope in `agents/Aevatar.Agents.GodGPT/GodChat/GodChatConversions.cs` (pack/unpack helpers)
- [ ] T004 [US1] Ensure generated C# types compile and are referenced by `agents/Aevatar.Agents.GodGPT` (update `.csproj` proto includes if required)

**Checkpoint**: New Protobuf contracts compile and can be packed into `EventEnvelope`.

---

## Phase 2: User Story 1 - Single-stream text delivery (Priority: P1) 🎯

**Goal**: ChatMiddleware consumes one envelope message type and always streams text quickly.

**Independent Test**: `scripts/test-ai-chat-flow.sh` passes (simple + long prompts), no SSE timeout, TTFT logged <= 2s p95.

- [ ] T101 [US1] Update producer: `agents/Aevatar.Agents.GodGPT/AIAgentStatusProxy/AIAgentStatusProxy.cs` to publish `GodChatStreamEnvelopeProto{text}` instead of any `ResponseStreamGodChatProto`
- [ ] T102 [US1] Implement `seq` allocation for text chunks in `AIAgentStatusProxy` (monotonic per chat stream)
- [ ] T103 [US1] Preserve aggregation behavior (first token immediate, then aggregated flush) while mapping into `TextChunkProto`
- [ ] T104 [US1] Update consumer: `apps/Aevatar.App/src/Aevatar.App.HttpApi.Host/Handler/ChatMiddleware.cs` to unpack `GodChatStreamEnvelopeProto` and write SSE based on `payload` oneof
- [ ] T105 [US1] Add explicit control messages: publish `control.text_completed` and close SSE when `control.all_completed` (or when text-only mode)
- [ ] T106 [US1] Add logging in ChatMiddleware for first envelope received and for ignored payload types (no silent returns)

---

## Phase 3: User Story 2 - Voice chat text-first, audio async, still one stream (Priority: P1)

**Goal**: Voice chat does not block text TTFT; audio arrives later on same stream.

**Independent Test**: Extend `scripts/test-ai-chat-flow.sh` to run a voice SSE scenario and validate both text and audio payloads on same connection.

- [ ] T201 [US2] Create `VoiceSynthesisGAgent` in `agents/Aevatar.Agents.GodGPT/VoiceSynthesis/VoiceSynthesisGAgent.cs` (parameterless ctor, Protobuf state/config if needed)
- [ ] T202 [US2] Add handler to accept `VoiceSynthesisJobProto` and perform sentence-level accumulation + TTS generation (migrate logic from `GodChatGAgent.Callbacks.cs`)
- [ ] T203 [US2] Publish `GodChatStreamEnvelopeProto{audio}` back to `GetStream(streamId, "GodChat")` with `text_seq_ref`/`sentence_index` for correlation
- [ ] T204 [US2] Update `AIAgentStatusProxy` to emit voice jobs when `IsVoiceChat=true` (fire-and-forget internal event) while still publishing text envelopes immediately
- [ ] T205 [US2] Update ChatMiddleware SSE output to include audio payload fields (base64 audio + metadata) without changing subscription model
- [ ] T206 [US2] Define completion semantics: `control.audio_completed` and `control.all_completed` (text completion should not wait for audio, but all_completed should)

---

## Phase 4: User Story 3 - Contract de-duplication and cleanup (Priority: P2)

**Goal**: Remove ambiguity from multiple `ResponseStreamGodChatProto` variants for streaming output.

**Independent Test**: grep confirms streaming output path no longer references old proto messages; build + script passes.

- [ ] T301 [US3] Identify and remove all client-streaming dependencies on `ResponseStreamGodChatProto` in producers/consumers (keep only for legacy internal uses if still needed)
- [ ] T302 [US3] Delete unused duplicate message definition in `agents/Aevatar.Agents.GodGPT/Protos/chat_manager_events.proto` (if confirmed unused by build/grep)
- [ ] T303 [US3] Remove ambiguous conversion helpers or rename them to prevent accidental namespace binding (`ChatManagerConversions.ToProto` vs `GodChatConversions.ToProto`)
- [ ] T304 [US3] Update local scripts/logging to validate payload TypeUrl equals the new envelope’s TypeUrl

---

## Phase 5: Validation & Performance

- [ ] T401 [US1] Update `scripts/test-ai-chat-flow.sh` to assert envelope payloads are received (and report TTFT + message counts)
- [ ] T402 [US2] Add a voice streaming test case in `scripts/test-ai-chat-flow.sh` (text first, audio later)
- [ ] T403 [US1] Run local build + services start + full script; capture TTFT, message count, and verify no SSE timeouts

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion - BLOCKS all user stories
- **User Stories (Phase 3+)**: All depend on Foundational phase completion
  - User stories can then proceed in parallel (if staffed)
  - Or sequentially in priority order (P1 → P2 → P3)
- **Polish (Final Phase)**: Depends on all desired user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Foundational (Phase 2) - No dependencies on other stories
- **User Story 2 (P2)**: Can start after Foundational (Phase 2) - May integrate with US1 but should be independently testable
- **User Story 3 (P3)**: Can start after Foundational (Phase 2) - May integrate with US1/US2 but should be independently testable

### Within Each User Story

- Tests (if included) MUST be written and FAIL before implementation
- Models before services
- Services before endpoints
- Core implementation before integration
- Story complete before moving to next priority

### Parallel Opportunities

- All Setup tasks marked [P] can run in parallel
- All Foundational tasks marked [P] can run in parallel (within Phase 2)
- Once Foundational phase completes, all user stories can start in parallel (if team capacity allows)
- All tests for a user story marked [P] can run in parallel
- Models within a story marked [P] can run in parallel
- Different user stories can be worked on in parallel by different team members

---

## Parallel Example: User Story 1

```bash
# Launch all tests for User Story 1 together (if tests requested):
Task: "Contract test for [endpoint] in tests/contract/test_[name].py"
Task: "Integration test for [user journey] in tests/integration/test_[name].py"

# Launch all models for User Story 1 together:
Task: "Create [Entity1] model in src/models/[entity1].py"
Task: "Create [Entity2] model in src/models/[entity2].py"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL - blocks all stories)
3. Complete Phase 3: User Story 1
4. **STOP and VALIDATE**: Test User Story 1 independently
5. Deploy/demo if ready

### Incremental Delivery

1. Complete Setup + Foundational → Foundation ready
2. Add User Story 1 → Test independently → Deploy/Demo (MVP!)
3. Add User Story 2 → Test independently → Deploy/Demo
4. Add User Story 3 → Test independently → Deploy/Demo
5. Each story adds value without breaking previous stories

### Parallel Team Strategy

With multiple developers:

1. Team completes Setup + Foundational together
2. Once Foundational is done:
   - Developer A: User Story 1
   - Developer B: User Story 2
   - Developer C: User Story 3
3. Stories complete and integrate independently

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story should be independently completable and testable
- Verify tests fail before implementing
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- Avoid: vague tasks, same file conflicts, cross-story dependencies that break independence

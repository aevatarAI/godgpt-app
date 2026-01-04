# Quickstart: Unified GodChat Stream Envelope (Text + Audio)

## Goal
Validate that HttpApi consumes a single stream message type (`GodChatStreamEnvelopeProto`) and that both text and audio are delivered on the same SSE connection.

## Local Run

```bash
cd /Users/liyingpei/Desktop/Code/godgpt-app
cd scripts && ./stop-services.sh && ./start-services.sh
```

## Verification

```bash
cd /Users/liyingpei/Desktop/Code/godgpt-app
API_URL="http://localhost:8082" AUTH_URL="http://localhost:8001" ./scripts/test-ai-chat-flow.sh
```

## Expected Signals
- ChatMiddleware logs a first message quickly (TTFT <= 2s p95).
- No silent drops due to payload type mismatch.
- For voice chat: text arrives first; audio arrives later, both on the same SSE stream.

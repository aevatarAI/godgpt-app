#!/bin/bash

# Test script for Awakening async generation
# This script will:
# 1. Reset awakening state
# 2. Create a session and send a message
# 3. Trigger async generation
# 4. Wait and query again to verify content generation

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/test-common.sh"

detect_urls
check_services

log_info "========================================"
log_info "Awakening Async Generation Test"
log_info "========================================"
echo ""

# Step 1: Login
if ! login; then
  exit 1
fi
echo ""

# Step 2: Reset awakening state
log_step "Resetting awakening state..."
RESET_RESPONSE=$(api_post "/api/godgpt/awakening/reset-state-for-testing" "{}")
RESET_SUCCESS=$(echo "$RESET_RESPONSE" | jq -r '.message // .data.message // empty')

if [ -n "$RESET_SUCCESS" ]; then
  log_info "Awakening state reset successfully ✓"
else
  log_warn "Reset response:"
  log_response "$RESET_RESPONSE"
fi
echo ""

# Step 3: Create a session
log_step "Creating a session..."
SESSION_RESPONSE=$(api_post "/api/godgpt/create-session" '{"guider":""}')

SESSION_ID=$(echo "$SESSION_RESPONSE" | jq -r '.data // empty')

if [ -z "$SESSION_ID" ] || [ "$SESSION_ID" = "null" ]; then
  echo -e "${RED}[ERROR]${NC} Failed to create session"
  echo "Response: $SESSION_RESPONSE"
  exit 1
fi

echo -e "${GREEN}[INFO]${NC} Session created: $SESSION_ID ✓"
echo ""

# Step 4: Send a message to the session
log_step "Sending message to session..."
MESSAGE_RESPONSE=$(api_post "/api/godgpt/chat/$SESSION_ID" \
  '{"message":"Hello, tell me about yourself and your purpose","userLocalTime":"2026-01-15T10:30:00.000Z"}')

log_response "$MESSAGE_RESPONSE"

echo -e "${GREEN}[INFO]${NC} Message sent ✓"
echo ""

# Wait a bit for the message to be processed
echo -e "${BLUE}[STEP]${NC} Waiting 3 seconds for message processing..."
sleep 3
echo ""

# Step 5: First call - should trigger async generation
log_step "First call - triggering async generation..."
FIRST_RESPONSE=$(api_get "/api/godgpt/awakening/today" "GodgptLanguage: English")

echo -e "${YELLOW}[RESPONSE]${NC}"
echo "$FIRST_RESPONSE" | jq '.'

STATUS=$(echo "$FIRST_RESPONSE" | jq -r '.data.status // .status // empty')
echo ""
echo -e "${GREEN}[INFO]${NC} Status: $STATUS"

if [ "$STATUS" = "1" ] || [ "$STATUS" = "2" ]; then
  echo -e "${GREEN}[INFO]${NC} First call completed ✓"
else
  echo -e "${YELLOW}[WARN]${NC} Unexpected status: $STATUS"
fi
echo ""

# Step 6: Wait for async generation to complete
echo -e "${BLUE}[STEP]${NC} Waiting 15 seconds for async generation to complete..."
sleep 15
echo ""

# Step 7: Second call - should query from State
log_step "Second call - querying from State..."
SECOND_RESPONSE=$(api_get "/api/godgpt/awakening/today" "GodgptLanguage: English")

echo -e "${YELLOW}[RESPONSE]${NC}"
echo "$SECOND_RESPONSE" | jq '.'

STATUS2=$(echo "$SECOND_RESPONSE" | jq -r '.data.status // .status // empty')
LEVEL=$(echo "$SECOND_RESPONSE" | jq -r '.data.awakeningLevel // .awakeningLevel // 0')
MESSAGE_LENGTH=$(echo "$SECOND_RESPONSE" | jq -r '.data.awakeningMessage // .awakeningMessage // "" | length')

echo ""
echo -e "${GREEN}[INFO]${NC} Status: $STATUS2"
echo -e "${GREEN}[INFO]${NC} Level: $LEVEL"
echo -e "${GREEN}[INFO]${NC} Message Length: $MESSAGE_LENGTH"

if [ "$STATUS2" = "2" ] && [ "$MESSAGE_LENGTH" -gt 0 ]; then
  echo -e "${GREEN}[INFO]${NC} ✓ Content generated successfully!"
elif [ "$STATUS2" = "1" ]; then
  echo -e "${YELLOW}[INFO]${NC} ⏳ Still generating, wait longer..."
elif [ "$STATUS2" = "2" ] && [ "$MESSAGE_LENGTH" -eq 0 ]; then
  echo -e "${YELLOW}[WARN]${NC} ⚠ Status is Completed but message is empty"
else
  echo -e "${YELLOW}[WARN]${NC} ⚠ Unexpected result"
fi

echo ""
echo -e "${GREEN}[INFO]${NC} ========================================"
echo -e "${GREEN}[INFO]${NC} Test Complete!"
echo -e "${GREEN}[INFO]${NC} ========================================"

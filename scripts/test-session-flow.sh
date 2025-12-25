#!/bin/bash

# =============================================================================
# Test Session Flow Script
# Tests GodGPTSessionController endpoints (session CRUD operations)
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Load common test utilities
source "$SCRIPT_DIR/test-common.sh"

# Variables
SESSION_ID=""

# =============================================================================
# Step 2: Create a new session
# =============================================================================
create_session() {
    log_step "Step 2: Creating a new chat session..."
    
    RESPONSE=$(api_post "/api/godgpt/create-session" '{"guider": "", "userLocalTime": "2024-12-22T10:00:00Z"}' "X-GodGPT-Language: English"$'\n'"X-GodGPT-AppType: ios")
    
    # Response is a raw GUID string
    SESSION_ID=$(echo "$RESPONSE" | tr -d '"')
    
    if [ -z "$SESSION_ID" ] || [ "$SESSION_ID" == "null" ]; then
        log_error "Failed to create session"
        log_response "$RESPONSE"
        exit 1
    fi
    
    log_info "Successfully created session"
    log_info "Session ID: ${SESSION_ID}"
}

# =============================================================================
# Step 3: Get session list
# =============================================================================
get_session_list() {
    log_step "Step 3: Getting session list..."
    
    RESPONSE=$(api_get "/api/godgpt/session-list" "X-GodGPT-Language: English")
    
    SESSION_COUNT=$(echo "$RESPONSE" | jq '. | length' 2>/dev/null || echo "0")
    
    log_info "Found ${SESSION_COUNT} session(s)"
    log_response "$RESPONSE"
}

# =============================================================================
# Step 4: Get session info
# =============================================================================
get_session_info() {
    log_step "Step 4: Getting session info for ${SESSION_ID}..."
    
    RESPONSE=$(api_get "/api/godgpt/session-info/${SESSION_ID}")
    
    log_info "Session info retrieved"
    log_response "$RESPONSE"
}

# =============================================================================
# Step 5: Get session messages
# =============================================================================
get_session_messages() {
    log_step "Step 5: Getting session messages for ${SESSION_ID}..."
    
    RESPONSE=$(api_get "/api/godgpt/chat/${SESSION_ID}" "X-GodGPT-Language: English")
    
    MESSAGE_COUNT=$(echo "$RESPONSE" | jq '. | length' 2>/dev/null || echo "0")
    
    log_info "Found ${MESSAGE_COUNT} message(s) in session"
    log_response "$RESPONSE"
}

# =============================================================================
# Step 6: Search sessions
# =============================================================================
search_sessions() {
    log_step "Step 6: Searching sessions with keyword 'test'..."
    
    RESPONSE=$(api_get "/api/godgpt/sessions/search?keyword=test")
    
    RESULT_COUNT=$(echo "$RESPONSE" | jq '. | length' 2>/dev/null || echo "0")
    
    log_info "Found ${RESULT_COUNT} matching session(s)"
    log_response "$RESPONSE"
}

# =============================================================================
# Step 7: Rename session
# =============================================================================
rename_session() {
    log_step "Step 7: Renaming session ${SESSION_ID}..."
    
    RESPONSE=$(api_put "/api/godgpt/chat/rename" "{\"sessionId\": \"${SESSION_ID}\", \"title\": \"Test Session Renamed\"}")
    
    RENAMED_ID=$(echo "$RESPONSE" | tr -d '"')
    
    if [ "$RENAMED_ID" == "$SESSION_ID" ]; then
        log_info "Successfully renamed session"
    else
        log_warn "Rename response: ${RESPONSE}"
    fi
}

# =============================================================================
# Step 8: Delete session
# =============================================================================
delete_session() {
    log_step "Step 8: Deleting session ${SESSION_ID}..."
    
    RESPONSE=$(api_delete "/api/godgpt/chat/${SESSION_ID}")
    
    DELETED_ID=$(echo "$RESPONSE" | tr -d '"')
    
    if [ "$DELETED_ID" == "$SESSION_ID" ]; then
        log_info "Successfully deleted session"
    else
        log_warn "Delete response: ${RESPONSE}"
    fi
}

# =============================================================================
# Step 9: Verify deletion
# =============================================================================
verify_deletion() {
    log_step "Step 9: Verifying session was deleted..."
    
    RESPONSE=$(api_get "/api/godgpt/session-list")
    
    # Check if session still exists
    if echo "$RESPONSE" | jq -e ".[] | select(.sessionId == \"${SESSION_ID}\")" > /dev/null 2>&1; then
        log_warn "Session still exists in list"
    else
        log_info "Session successfully removed from list"
    fi
}

# =============================================================================
# Main execution
# =============================================================================
main() {
    echo "=========================================="
    echo "GodGPT Session Flow Test"
    echo "=========================================="
    echo ""
    
    if ! login; then
        log_error "Login failed, aborting tests"
        exit 1
    fi
    echo ""
    
    create_session
    echo ""
    
    get_session_list
    echo ""
    
    get_session_info
    echo ""
    
    get_session_messages
    echo ""
    
    search_sessions
    echo ""
    
    rename_session
    echo ""
    
    delete_session
    echo ""
    
    verify_deletion
    echo ""
    
    echo "=========================================="
    log_info "Session Flow Test Complete!"
    echo "=========================================="
}

main "$@"


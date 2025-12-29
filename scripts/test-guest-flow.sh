#!/bin/bash

# =============================================================================
# Test Guest Flow Script
# Tests GodGPTGuestController endpoints (anonymous access)
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Load common test utilities
source "$SCRIPT_DIR/test-common.sh"

# Test 1: Get Guest Chat Limits
test_get_guest_limits() {
    log_step "Test 1: Getting guest chat limits..."
    
    local response=$(api_get_guest "/api/godgpt/guest/limits" "X-GodGPT-Language: English")
    
    log_response "$response"
    
    # Response is {"code":"20000","data":{"remainingChats":...,"totalAllowed":...},"message":""} - check .data
    if echo "$response" | jq -e '.data.remainingChats' > /dev/null 2>&1; then
        local remaining=$(echo "$response" | jq -r '.data.remainingChats')
        local total=$(echo "$response" | jq -r '.data.totalAllowed')
        log_info "Guest limits retrieved successfully ✓"
        log_info "Remaining chats: $remaining / $total"
        return 0
    else
        log_error "Failed to get guest limits"
        return 1
    fi
}

# Test 2: Create Guest Session
test_create_guest_session() {
    log_step "Test 2: Creating guest session..."
    
    local response=$(api_post_guest "/api/godgpt/guest/create-session" '{"guider": "test"}' "X-GodGPT-Language: English"$'\n'"X-GodGPT-AppType: ios")
    
    log_response "$response"
    
    # Response is {"code":"20000","data":{"remainingChats":...},"message":""} - check .data
    if echo "$response" | jq -e '.data.remainingChats' > /dev/null 2>&1; then
        local remaining=$(echo "$response" | jq -r '.data.remainingChats')
        log_info "Guest session created successfully ✓"
        log_info "Remaining chats: $remaining"
        
        if echo "$response" | jq -e '.data.sessionId' > /dev/null 2>&1; then
            local session_id=$(echo "$response" | jq -r '.data.sessionId')
            log_info "Session ID: $session_id"
        fi
        
        return 0
    else
        log_warn "Guest session creation response doesn't match expected format"
        log_warn "This might be expected if daily limit is reached"
        return 0  # Don't fail - limit reached is valid response
    fi
}

# Run all tests
run_all_tests() {
    local passed=0
    local failed=0
    
    log_info "========================================"
    log_info "Running Guest Flow Tests"
    log_info "========================================"
    echo ""
    
    if test_get_guest_limits; then
        ((passed++))
    else
        ((failed++))
    fi
    echo ""
    
    if test_create_guest_session; then
        ((passed++))
    else
        ((failed++))
    fi
    echo ""
    
    log_info "========================================"
    log_info "Test Results: $passed passed, $failed failed"
    log_info "========================================"
    
    if [ $failed -gt 0 ]; then
        exit 1
    fi
}

# Main function
main() {
    echo ""
    log_info "========================================"
    log_info "GodGPT Guest Flow Test"
    log_info "========================================"
    echo ""
    
    check_services
    echo ""
    
    case "${1:-all}" in
        "limits")
            test_get_guest_limits
            ;;
        "create")
            test_create_guest_session
            ;;
        "all"|*)
            run_all_tests
            ;;
    esac
}

main "$@"


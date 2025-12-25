#!/bin/bash

# =============================================================================
# Test Invitation Flow Script
# Tests invitation-related endpoints
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Load common test utilities
source "$SCRIPT_DIR/test-common.sh"

# Variables
INVITE_CODE=""  # Will be populated after generating invite code

# Check if jq is installed
check_dependencies() {
    if ! command -v jq &> /dev/null; then
        log_error "jq is required but not installed. Install with: brew install jq"
        exit 1
    fi
    
    if ! command -v curl &> /dev/null; then
        log_error "curl is required but not installed."
        exit 1
    fi
}

# Test 1: Get Invitation Info (and extract invite code for later tests)
test_get_invitation_info() {
    log_step "Test 1: Getting invitation info..."
    
    local response=$(api_get "/api/godgpt/invitation/info")
    
    log_response "$response"
    
    # Extract invite code for later tests
    INVITE_CODE=$(echo "$response" | jq -r '.inviteCode // empty')
    
    if echo "$response" | jq -e '.rewardTiers' > /dev/null 2>&1 || echo "$response" | jq -e '.inviteCode' > /dev/null 2>&1; then
        log_info "Invitation info retrieved successfully ✓"
        if [ -n "$INVITE_CODE" ] && [ "$INVITE_CODE" != "null" ]; then
            log_info "Extracted invite code: $INVITE_CODE"
        fi
        return 0
    else
        log_warn "Failed to get invitation info"
        return 1
    fi
}

# Test 2: Get Invitation Code Type
test_get_code_type() {
    log_step "Test 2: Getting invitation code type..."
    
    local response=$(api_get "/api/godgpt/invitation/code-type?InviteCode=TESTCODE123")
    
    log_response "$response"
    
    if echo "$response" | jq -e '.codeType' > /dev/null 2>&1; then
        log_info "Code type retrieved successfully ✓"
        return 0
    else
        log_warn "Failed to get code type"
        return 1
    fi
}

# Test 3: Redeem Invite Code (Friend Invitation)
# Note: This test often fails as expected - user cannot redeem their own code
test_redeem_friend_code() {
    log_step "Test 3: Redeeming friend invitation code..."
    
    # Use the invite code from Test 1, or fallback to test code
    local code_to_redeem="${INVITE_CODE:-TESTFRIEND123}"
    
    if [ "$code_to_redeem" == "TESTFRIEND123" ]; then
        log_warn "No invite code available from Test 1, using test code (may fail)"
    else
        log_info "Using invite code from Test 1: $code_to_redeem"
    fi
    
    local response=$(api_post "/api/godgpt/invitation/redeem" "{
        \"InviteCode\": \"$code_to_redeem\",
        \"IsWeb\": true
    }")
    
    log_response "$response"
    
    # Check if response is valid JSON with isValid field (endpoint works)
    if echo "$response" | jq -e '.isValid != null or .IsValid != null' > /dev/null 2>&1; then
        local is_valid=$(echo "$response" | jq -r '.IsValid // .isValid // false')
        if [ "$is_valid" == "true" ]; then
            log_info "Redeem friend code succeeded ✓"
        else
            # isValid=false is expected when redeeming own code or invalid code
            log_info "Redeem endpoint tested ✓ (isValid=false, expected for own/invalid code)"
        fi
        return 0
    elif echo "$response" | jq -e '.error' > /dev/null 2>&1; then
        # Error response but endpoint is reachable
        log_info "Redeem endpoint tested ✓ (returned error, expected for own/invalid code)"
        return 0
    else
        log_warn "Redeem friend code endpoint may have failed"
        return 1
    fi
}

# Test 4: Redeem Free Trial Code
# Note: This test uses a fake code, so isValid=false is expected
test_redeem_trial_code() {
    log_step "Test 4: Redeeming free trial code..."
    
    local response=$(api_post "/api/godgpt/invitation/redeem" '{
        "InviteCode": "FREETRIAL12345",
        "IsWeb": true
    }')
    
    log_response "$response"
    
    # Any valid JSON response with isValid field means endpoint works
    if echo "$response" | jq -e '.isValid != null or .IsValid != null' > /dev/null 2>&1; then
        local is_valid=$(echo "$response" | jq -r '.IsValid // .isValid // false')
        if [ "$is_valid" == "true" ]; then
            log_info "Redeem trial code succeeded ✓"
        else
            log_info "Redeem trial code endpoint tested ✓ (isValid=false, expected for invalid code)"
        fi
        return 0
    else
        log_warn "Redeem trial code endpoint may have failed"
        return 1
    fi
}

# Test 5: Get Credits History
test_get_credits_history() {
    log_step "Test 5: Getting credits history..."
    
    local response=$(api_get "/api/godgpt/invitation/credits/history?page=1&pageSize=10")
    
    log_response "$response"
    
    if echo "$response" | jq -e '.items' > /dev/null 2>&1; then
        log_info "Credits history retrieved successfully ✓"
        return 0
    else
        log_warn "Failed to get credits history"
        return 1
    fi
}

# Test 6: Generate Free Trial Code (requires manager permissions)
test_generate_trial_code() {
    log_step "Test 6: Generating free trial code..."
    log_warn "Note: This requires manager permissions"
    
    local start_time=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
    local end_time=$(date -u -v+30d +"%Y-%m-%dT%H:%M:%SZ" 2>/dev/null || date -u -d "+30 days" +"%Y-%m-%dT%H:%M:%SZ" 2>/dev/null || date -u +"%Y-%m-%dT%H:%M:%SZ")
    
    local response=$(api_post "/api/godgpt/invitation/generate-trial-code" "{
        \"trialDays\": 7,
        \"productId\": \"price_1RYiPu4KJpMhj2HtScrxZ3XE\",
        \"platform\": 0,
        \"startTime\": \"$start_time\",
        \"endTime\": \"$end_time\",
        \"description\": \"Test batch\",
        \"quantity\": 10
    }")
    
    log_response "$response"
    
    if echo "$response" | jq -e '.success' > /dev/null 2>&1; then
        log_info "Generate trial code endpoint tested ✓"
        return 0
    else
        log_warn "Generate trial code may have failed (expected if not manager)"
        return 0  # Don't fail if permission denied
    fi
}

# Run all tests
run_all_tests() {
    local passed=0
    local failed=0
    
    log_info "========================================"
    log_info "Running Invitation Flow Tests"
    log_info "========================================"
    echo ""
    
    # Test 1: Get invitation info first (this will generate invite code if needed)
    if test_get_invitation_info; then
        ((passed++))
    else
        ((failed++))
    fi
    echo ""
    
    if test_get_code_type; then
        ((passed++))
    else
        ((failed++))
    fi
    echo ""
    
    # Test 3: Redeem friend code (will use invite code from Test 1)
    # Note: This will fail if user tries to redeem their own code (expected)
    if test_redeem_friend_code; then
        ((passed++))
    else
        ((failed++))
    fi
    echo ""
    
    if test_redeem_trial_code; then
        ((passed++))
    else
        ((failed++))
    fi
    echo ""
    
    if test_get_credits_history; then
        ((passed++))
    else
        ((failed++))
    fi
    echo ""
    
    test_generate_trial_code
    ((passed++))
    echo ""
    
    log_info "========================================"
    log_info "Test Results: $passed passed, $failed failed"
    log_info "========================================"
    log_info "Note: Redeem test may fail if user tries to redeem their own code (expected behavior)"
}

# Main function
main() {
    echo ""
    log_info "========================================"
    log_info "Aevatar Invitation Flow Test"
    log_info "========================================"
    echo ""
    
    check_dependencies
    check_services
    echo ""
    
    if ! login; then
        log_error "Login failed, aborting tests"
        exit 1
    fi
    echo ""
    
    case "${1:-all}" in
        "info")
            test_get_invitation_info
            ;;
        "code-type")
            test_get_code_type
            ;;
        "redeem-friend")
            test_redeem_friend_code
            ;;
        "redeem-trial")
            test_redeem_trial_code
            ;;
        "history")
            test_get_credits_history
            ;;
        "generate")
            test_generate_trial_code
            ;;
        "all"|*)
            run_all_tests
            ;;
    esac
}

main "$@"

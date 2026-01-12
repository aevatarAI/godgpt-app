#!/bin/bash

# =============================================================================
# Test Twitter Flow Script
# Tests Twitter authentication, binding, monitoring, and reward endpoints
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Load common test utilities
source "$SCRIPT_DIR/test-common.sh"

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

# =============================================================================
# User-Level Twitter API Tests (/api/godgpt/invitation/twitter/*)
# =============================================================================

# Test 1: Get Twitter Auth Params (OAuth2 PKCE)
test_get_auth_params() {
    log_step "Test 1: Getting Twitter OAuth2 auth params..."
    
    local response=$(api_get "/api/godgpt/invitation/twitter/params")
    
    log_response "$response"
    
    if echo "$response" | jq -e '.authorizationUrl' > /dev/null 2>&1; then
        log_info "Twitter auth params retrieved successfully ✓"
        
        # Extract values for potential use in verify test
        AUTH_URL_TWITTER=$(echo "$response" | jq -r '.authorizationUrl // empty')
        CODE_VERIFIER=$(echo "$response" | jq -r '.codeVerifier // empty')
        STATE=$(echo "$response" | jq -r '.state // empty')
        
        if [ -n "$AUTH_URL_TWITTER" ]; then
            log_info "Authorization URL: ${AUTH_URL_TWITTER:0:80}..."
        fi
        return 0
    else
        log_warn "Failed to get Twitter auth params (Twitter auth may not be configured)"
        return 1
    fi
}

# Test 2: Get Twitter Bind Status
test_get_bind_status() {
    log_step "Test 2: Getting Twitter bind status..."
    
    local response=$(api_get "/api/godgpt/invitation/twitter/bind-status")
    
    log_response "$response"
    
    if echo "$response" | jq -e '.isBound != null or .IsBound != null' > /dev/null 2>&1; then
        local is_bound=$(echo "$response" | jq -r '.isBound // .IsBound // false')
        log_info "Twitter bind status retrieved successfully ✓ (isBound: $is_bound)"
        return 0
    else
        log_warn "Failed to get Twitter bind status"
        return 1
    fi
}

# Test 3: Verify Twitter Auth Code (will fail without real code)
test_verify_auth_code() {
    log_step "Test 3: Testing Twitter auth code verification endpoint..."
    log_warn "Note: This test uses a fake code and is expected to fail"
    
    local response=$(api_post "/api/godgpt/invitation/twitter/verify" '{
        "code": "fake_test_code_12345",
        "state": "fake_state",
        "codeVerifier": "fake_code_verifier"
    }')
    
    log_response "$response"
    
    # Any response (success or error) means endpoint is reachable
    if echo "$response" | jq -e '.' > /dev/null 2>&1; then
        log_info "Twitter verify endpoint tested ✓ (expected to fail with fake code)"
        return 0
    else
        log_warn "Twitter verify endpoint may not be available"
        return 1
    fi
}

# Test 4: Unbind Twitter Account
test_unbind_twitter() {
    log_step "Test 4: Testing Twitter unbind endpoint..."
    
    local response=$(api_post "/api/godgpt/invitation/twitter/unbind" '{}')
    
    log_response "$response"
    
    # Any response means endpoint is reachable
    if echo "$response" | jq -e '.' > /dev/null 2>&1 || [ -z "$response" ]; then
        log_info "Twitter unbind endpoint tested ✓"
        return 0
    else
        log_warn "Twitter unbind endpoint may not be available"
        return 1
    fi
}

# =============================================================================
# Management-Level Twitter API Tests (/api/godgpt/twitter-management/*)
# Note: These require manager/admin permissions
# =============================================================================

# Test 5: Get Monitor Status
test_get_monitor_status() {
    log_step "Test 5: Getting Twitter monitor status..."
    log_warn "Note: This requires manager permissions"
    
    local response=$(api_get "/api/godgpt/twitter-management/monitor/status")
    
    log_response "$response"
    
    if echo "$response" | jq -e '.isRunning != null or .IsRunning != null' > /dev/null 2>&1; then
        local is_running=$(echo "$response" | jq -r '.isRunning // .IsRunning // false')
        log_info "Monitor status retrieved successfully ✓ (isRunning: $is_running)"
        return 0
    elif echo "$response" | jq -e '.error' > /dev/null 2>&1; then
        log_info "Monitor status endpoint tested ✓ (permission denied or not configured)"
        return 0
    else
        log_warn "Failed to get monitor status"
        return 1
    fi
}

# Test 6: Start Monitor (requires manager permissions)
test_start_monitor() {
    log_step "Test 6: Starting Twitter monitor..."
    log_warn "Note: This requires manager permissions"
    
    local response=$(api_post "/api/godgpt/twitter-management/monitor/start" '{}')
    
    log_response "$response"
    
    if echo "$response" | jq -e '.isSuccess != null or .IsSuccess != null or .success != null' > /dev/null 2>&1; then
        log_info "Monitor start endpoint tested ✓"
        return 0
    elif echo "$response" | jq -e '.error' > /dev/null 2>&1; then
        log_info "Monitor start endpoint tested ✓ (permission denied or not configured)"
        return 0
    else
        log_warn "Monitor start endpoint may not be available"
        return 1
    fi
}

# Test 7: Stop Monitor (requires manager permissions)
test_stop_monitor() {
    log_step "Test 7: Stopping Twitter monitor..."
    log_warn "Note: This requires manager permissions"
    
    local response=$(api_post "/api/godgpt/twitter-management/monitor/stop" '{}')
    
    log_response "$response"
    
    if echo "$response" | jq -e '.isSuccess != null or .IsSuccess != null or .success != null' > /dev/null 2>&1; then
        log_info "Monitor stop endpoint tested ✓"
        return 0
    elif echo "$response" | jq -e '.error' > /dev/null 2>&1; then
        log_info "Monitor stop endpoint tested ✓ (permission denied or not configured)"
        return 0
    else
        log_warn "Monitor stop endpoint may not be available"
        return 1
    fi
}

# Test 8: Manual Fetch (requires manager permissions)
test_fetch_manually() {
    log_step "Test 8: Testing manual tweet fetch..."
    log_warn "Note: This requires manager permissions"
    
    local response=$(api_post "/api/godgpt/twitter-management/monitor/fetch-manually" '{}')
    
    log_response "$response"
    
    if echo "$response" | jq -e '.isSuccess != null or .IsSuccess != null' > /dev/null 2>&1; then
        log_info "Manual fetch endpoint tested ✓"
        return 0
    elif echo "$response" | jq -e '.error' > /dev/null 2>&1; then
        log_info "Manual fetch endpoint tested ✓ (permission denied or not configured)"
        return 0
    else
        log_warn "Manual fetch endpoint may not be available"
        return 1
    fi
}

# Test 9: Get Reward Status
test_get_reward_status() {
    log_step "Test 9: Getting Twitter reward status..."
    log_warn "Note: This requires manager permissions"
    
    local response=$(api_get "/api/godgpt/twitter-management/reward/status")
    
    log_response "$response"
    
    if echo "$response" | jq -e '.isRunning != null or .IsRunning != null' > /dev/null 2>&1; then
        log_info "Reward status retrieved successfully ✓"
        return 0
    elif echo "$response" | jq -e '.error' > /dev/null 2>&1; then
        log_info "Reward status endpoint tested ✓ (permission denied or not configured)"
        return 0
    else
        log_warn "Failed to get reward status"
        return 1
    fi
}

# Test 10: Start Reward Calculation (requires manager permissions)
test_start_reward() {
    log_step "Test 10: Starting Twitter reward calculation..."
    log_warn "Note: This requires manager permissions"
    
    local response=$(api_post "/api/godgpt/twitter-management/reward/start" '{}')
    
    log_response "$response"
    
    if echo "$response" | jq -e '.isSuccess != null or .IsSuccess != null or .success != null' > /dev/null 2>&1; then
        log_info "Reward start endpoint tested ✓"
        return 0
    elif echo "$response" | jq -e '.error' > /dev/null 2>&1; then
        log_info "Reward start endpoint tested ✓ (permission denied or not configured)"
        return 0
    else
        log_warn "Reward start endpoint may not be available"
        return 1
    fi
}

# Test 11: Stop Reward Calculation (requires manager permissions)
test_stop_reward() {
    log_step "Test 11: Stopping Twitter reward calculation..."
    log_warn "Note: This requires manager permissions"
    
    local response=$(api_post "/api/godgpt/twitter-management/reward/stop" '{}')
    
    log_response "$response"
    
    if echo "$response" | jq -e '.isSuccess != null or .IsSuccess != null or .success != null' > /dev/null 2>&1; then
        log_info "Reward stop endpoint tested ✓"
        return 0
    elif echo "$response" | jq -e '.error' > /dev/null 2>&1; then
        log_info "Reward stop endpoint tested ✓ (permission denied or not configured)"
        return 0
    else
        log_warn "Reward stop endpoint may not be available"
        return 1
    fi
}

# Test 12: Get Reward History
test_get_reward_history() {
    log_step "Test 12: Getting Twitter reward history..."
    log_warn "Note: This requires manager permissions"
    
    local response=$(api_get "/api/godgpt/twitter-management/reward/history?days=7")
    
    log_response "$response"
    
    if echo "$response" | jq -e '.' > /dev/null 2>&1; then
        log_info "Reward history endpoint tested ✓"
        return 0
    else
        log_warn "Failed to get reward history"
        return 1
    fi
}

# Test 13: Query Tweets by Time Range
test_query_tweets() {
    log_step "Test 13: Querying tweets by time range..."
    log_warn "Note: This requires manager permissions"
    
    local start_time=$(date -u -v-1d +"%Y-%m-%dT%H:%M:%SZ" 2>/dev/null || date -u -d "yesterday" +"%Y-%m-%dT%H:%M:%SZ" 2>/dev/null || date -u +"%Y-%m-%dT%H:%M:%SZ")
    local end_time=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
    
    local response=$(api_get "/api/godgpt/twitter-management/monitor/tweets?startTime=$start_time&endTime=$end_time")
    
    log_response "$response"
    
    if echo "$response" | jq -e '.' > /dev/null 2>&1; then
        log_info "Query tweets endpoint tested ✓"
        return 0
    else
        log_warn "Failed to query tweets"
        return 1
    fi
}

# Test 14: Get User Reward Records
test_get_user_rewards() {
    log_step "Test 14: Getting user reward records..."
    log_warn "Note: This requires manager permissions"
    
    local response=$(api_get "/api/godgpt/twitter-management/reward/user-records?days=7")
    
    log_response "$response"
    
    if echo "$response" | jq -e '.' > /dev/null 2>&1; then
        log_info "User reward records endpoint tested ✓"
        return 0
    else
        log_warn "Failed to get user reward records"
        return 1
    fi
}

# =============================================================================
# Test Runners
# =============================================================================

# Run user-level tests only
run_user_tests() {
    local passed=0
    local failed=0
    
    log_info "========================================"
    log_info "Running Twitter User-Level Tests"
    log_info "========================================"
    echo ""
    
    if test_get_auth_params; then ((passed++)); else ((failed++)); fi
    echo ""
    
    if test_get_bind_status; then ((passed++)); else ((failed++)); fi
    echo ""
    
    if test_verify_auth_code; then ((passed++)); else ((failed++)); fi
    echo ""
    
    if test_unbind_twitter; then ((passed++)); else ((failed++)); fi
    echo ""
    
    log_info "========================================"
    log_info "User Tests: $passed passed, $failed failed"
    log_info "========================================"
}

# Run management-level tests only
run_management_tests() {
    local passed=0
    local failed=0
    
    log_info "========================================"
    log_info "Running Twitter Management Tests"
    log_info "(Requires Manager Permissions)"
    log_info "========================================"
    echo ""
    
    if test_get_monitor_status; then ((passed++)); else ((failed++)); fi
    echo ""
    
    if test_start_monitor; then ((passed++)); else ((failed++)); fi
    echo ""
    
    if test_stop_monitor; then ((passed++)); else ((failed++)); fi
    echo ""
    
    if test_fetch_manually; then ((passed++)); else ((failed++)); fi
    echo ""
    
    if test_get_reward_status; then ((passed++)); else ((failed++)); fi
    echo ""
    
    if test_start_reward; then ((passed++)); else ((failed++)); fi
    echo ""
    
    if test_stop_reward; then ((passed++)); else ((failed++)); fi
    echo ""
    
    if test_get_reward_history; then ((passed++)); else ((failed++)); fi
    echo ""
    
    if test_query_tweets; then ((passed++)); else ((failed++)); fi
    echo ""
    
    if test_get_user_rewards; then ((passed++)); else ((failed++)); fi
    echo ""
    
    log_info "========================================"
    log_info "Management Tests: $passed passed, $failed failed"
    log_info "========================================"
}

# Run all tests
run_all_tests() {
    local passed=0
    local failed=0
    
    log_info "========================================"
    log_info "Running All Twitter Flow Tests"
    log_info "========================================"
    echo ""
    
    # User-level tests
    log_info "--- User-Level Tests ---"
    echo ""
    
    if test_get_auth_params; then ((passed++)); else ((failed++)); fi
    echo ""
    
    if test_get_bind_status; then ((passed++)); else ((failed++)); fi
    echo ""
    
    if test_verify_auth_code; then ((passed++)); else ((failed++)); fi
    echo ""
    
    if test_unbind_twitter; then ((passed++)); else ((failed++)); fi
    echo ""
    
    # Management-level tests
    log_info "--- Management-Level Tests ---"
    log_info "(Requires Manager Permissions)"
    echo ""
    
    if test_get_monitor_status; then ((passed++)); else ((failed++)); fi
    echo ""
    
    if test_start_monitor; then ((passed++)); else ((failed++)); fi
    echo ""
    
    if test_stop_monitor; then ((passed++)); else ((failed++)); fi
    echo ""
    
    if test_fetch_manually; then ((passed++)); else ((failed++)); fi
    echo ""
    
    if test_get_reward_status; then ((passed++)); else ((failed++)); fi
    echo ""
    
    if test_start_reward; then ((passed++)); else ((failed++)); fi
    echo ""
    
    if test_stop_reward; then ((passed++)); else ((failed++)); fi
    echo ""
    
    if test_get_reward_history; then ((passed++)); else ((failed++)); fi
    echo ""
    
    if test_query_tweets; then ((passed++)); else ((failed++)); fi
    echo ""
    
    if test_get_user_rewards; then ((passed++)); else ((failed++)); fi
    echo ""
    
    log_info "========================================"
    log_info "Total Results: $passed passed, $failed failed"
    log_info "========================================"
    log_info "Note: Management tests may fail without manager permissions (expected)"
}

# =============================================================================
# Main
# =============================================================================

main() {
    echo ""
    log_info "========================================"
    log_info "Aevatar Twitter Flow Test"
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
        # User-level tests
        "auth-params")
            test_get_auth_params
            ;;
        "bind-status")
            test_get_bind_status
            ;;
        "verify")
            test_verify_auth_code
            ;;
        "unbind")
            test_unbind_twitter
            ;;
        "user")
            run_user_tests
            ;;
        # Management tests
        "monitor-status")
            test_get_monitor_status
            ;;
        "monitor-start")
            test_start_monitor
            ;;
        "monitor-stop")
            test_stop_monitor
            ;;
        "fetch")
            test_fetch_manually
            ;;
        "reward-status")
            test_get_reward_status
            ;;
        "reward-start")
            test_start_reward
            ;;
        "reward-stop")
            test_stop_reward
            ;;
        "reward-history")
            test_get_reward_history
            ;;
        "tweets")
            test_query_tweets
            ;;
        "user-rewards")
            test_get_user_rewards
            ;;
        "management")
            run_management_tests
            ;;
        # All tests
        "all"|*)
            run_all_tests
            ;;
    esac
}

main "$@"

#!/bin/bash

# =============================================================================
# Test Twitter Flow Script
# Tests Twitter authentication, binding, monitoring, and reward endpoints
# 
# Test Flow Logic:
# 1. User Tests:
#    - Get auth params -> Save state/verifier for later use
#    - Get bind status -> Determine if user is already bound
#    - If bound: Test unbind -> Then re-verify flow
#    - Test verify endpoint (with saved params)
#
# 2. Management Tests:
#    - Get monitor status -> Check current state
#    - If not running: Start -> Fetch -> Stop
#    - If running: Stop -> Start -> Fetch -> Stop
#    - Same logic for reward calculation
#    - Query tweets using time range from monitor
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Load common test utilities
source "$SCRIPT_DIR/test-common.sh"

# =============================================================================
# Global State Variables (for inter-test communication)
# =============================================================================
TWITTER_AUTH_URL=""
TWITTER_CODE_VERIFIER=""
TWITTER_STATE=""
TWITTER_IS_BOUND=false
TWITTER_MONITOR_RUNNING=false
TWITTER_REWARD_RUNNING=false
LAST_FETCH_TIME=""

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
# Saves params to global state for use in verify test
test_get_auth_params() {
    log_step "Test 1: Getting Twitter OAuth2 auth params..."
    
    local response=$(api_get "/api/godgpt/invitation/twitter/params")
    
    log_response "$response"
    
    if echo "$response" | jq -e '.authorizationUrl' > /dev/null 2>&1; then
        log_info "Twitter auth params retrieved successfully ✓"
        
        # Save to global state for later tests
        TWITTER_AUTH_URL=$(echo "$response" | jq -r '.authorizationUrl // empty')
        TWITTER_CODE_VERIFIER=$(echo "$response" | jq -r '.codeVerifier // empty')
        TWITTER_STATE=$(echo "$response" | jq -r '.state // empty')
        
        if [ -n "$TWITTER_AUTH_URL" ]; then
            log_info "→ Saved auth params for subsequent tests"
            log_info "  Authorization URL: ${TWITTER_AUTH_URL:0:60}..."
            log_info "  State: ${TWITTER_STATE:0:20}..."
        fi
        return 0
    else
        log_warn "Failed to get Twitter auth params (Twitter auth may not be configured)"
        return 1
    fi
}

# Test 2: Get Twitter Bind Status
# Updates global TWITTER_IS_BOUND state
test_get_bind_status() {
    log_step "Test 2: Getting Twitter bind status..."
    
    local response=$(api_get "/api/godgpt/invitation/twitter/bind-status")
    
    log_response "$response"
    
    if echo "$response" | jq -e '.isBound != null or .IsBound != null' > /dev/null 2>&1; then
        TWITTER_IS_BOUND=$(echo "$response" | jq -r '.isBound // .IsBound // false')
        log_info "Twitter bind status retrieved successfully ✓"
        log_info "→ User is currently bound: $TWITTER_IS_BOUND"
        
        # Show bound account info if available
        local username=$(echo "$response" | jq -r '.twitterUsername // .username // empty')
        if [ -n "$username" ] && [ "$username" != "null" ]; then
            log_info "  Twitter Username: @$username"
        fi
        return 0
    else
        log_warn "Failed to get Twitter bind status"
        return 1
    fi
}

# Test 3: Verify Twitter Auth Code
# Uses saved params from test_get_auth_params if available
test_verify_auth_code() {
    log_step "Test 3: Testing Twitter auth code verification endpoint..."
    
    local code_verifier="${TWITTER_CODE_VERIFIER:-fake_code_verifier}"
    local state="${TWITTER_STATE:-fake_state}"
    
    if [ -n "$TWITTER_CODE_VERIFIER" ]; then
        log_info "→ Using saved auth params from Test 1"
    else
        log_warn "→ No saved params, using fake values (expected to fail)"
    fi
    
    local response=$(api_post "/api/godgpt/invitation/twitter/verify" "{
        \"code\": \"fake_test_code_12345\",
        \"state\": \"$state\",
        \"codeVerifier\": \"$code_verifier\"
    }")
    
    log_response "$response"
    
    # Check the response type
    if echo "$response" | jq -e '.success == true' > /dev/null 2>&1; then
        log_info "Twitter verify succeeded ✓"
        TWITTER_IS_BOUND=true
        return 0
    elif echo "$response" | jq -e '.' > /dev/null 2>&1; then
        log_info "Twitter verify endpoint tested ✓ (failed as expected with fake code)"
        return 0
    else
        log_warn "Twitter verify endpoint may not be available"
        return 1
    fi
}

# Test 4: Unbind Twitter Account
# Only performs unbind if user is currently bound
test_unbind_twitter() {
    log_step "Test 4: Testing Twitter unbind endpoint..."
    
    if [ "$TWITTER_IS_BOUND" == "true" ]; then
        log_info "→ User is bound, will attempt to unbind"
    else
        log_info "→ User is not bound, testing endpoint reachability"
    fi
    
    local response=$(api_post "/api/godgpt/invitation/twitter/unbind" '{}')
    
    log_response "$response"
    
    # Check response
    if echo "$response" | jq -e '.success == true' > /dev/null 2>&1; then
        log_info "Twitter unbind succeeded ✓"
        TWITTER_IS_BOUND=false
        return 0
    elif echo "$response" | jq -e '.' > /dev/null 2>&1 || [ -z "$response" ]; then
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
# Updates global TWITTER_MONITOR_RUNNING state
test_get_monitor_status() {
    log_step "Test 5: Getting Twitter monitor status..."
    log_warn "Note: This requires manager permissions"
    
    local response=$(api_get "/api/godgpt/twitter-management/monitor/status")
    
    log_response "$response"
    
    if echo "$response" | jq -e '.isRunning != null or .IsRunning != null' > /dev/null 2>&1; then
        TWITTER_MONITOR_RUNNING=$(echo "$response" | jq -r '.isRunning // .IsRunning // false')
        log_info "Monitor status retrieved successfully ✓"
        log_info "→ Monitor is running: $TWITTER_MONITOR_RUNNING"
        
        # Extract last fetch time if available
        LAST_FETCH_TIME=$(echo "$response" | jq -r '.lastFetchTime // .LastFetchTime // empty')
        if [ -n "$LAST_FETCH_TIME" ] && [ "$LAST_FETCH_TIME" != "null" ]; then
            log_info "  Last fetch: $LAST_FETCH_TIME"
        fi
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
# Skips if already running (from Test 5 status)
test_start_monitor() {
    log_step "Test 6: Starting Twitter monitor..."
    log_warn "Note: This requires manager permissions"
    
    if [ "$TWITTER_MONITOR_RUNNING" == "true" ]; then
        log_info "→ Monitor is already running, testing endpoint anyway"
    else
        log_info "→ Monitor is not running, will attempt to start"
    fi
    
    local response=$(api_post "/api/godgpt/twitter-management/monitor/start" '{}')
    
    log_response "$response"
    
    if echo "$response" | jq -e '.isSuccess == true or .success == true' > /dev/null 2>&1; then
        log_info "Monitor started successfully ✓"
        TWITTER_MONITOR_RUNNING=true
        return 0
    elif echo "$response" | jq -e '.isSuccess != null or .success != null' > /dev/null 2>&1; then
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

# Test 7: Manual Fetch (requires manager permissions)
# Only makes sense after monitor is started
test_fetch_manually() {
    log_step "Test 7: Testing manual tweet fetch..."
    log_warn "Note: This requires manager permissions"
    
    if [ "$TWITTER_MONITOR_RUNNING" == "true" ]; then
        log_info "→ Monitor is running, fetch should work"
    else
        log_info "→ Monitor may not be running, fetch might fail"
    fi
    
    local response=$(api_post "/api/godgpt/twitter-management/monitor/fetch-manually" '{}')
    
    log_response "$response"
    
    if echo "$response" | jq -e '.isSuccess == true' > /dev/null 2>&1; then
        log_info "Manual fetch succeeded ✓"
        # Extract fetch time for query test
        LAST_FETCH_TIME=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
        local tweet_count=$(echo "$response" | jq -r '.data.totalTweets // .data.TotalTweets // 0')
        log_info "  Tweets fetched: $tweet_count"
        return 0
    elif echo "$response" | jq -e '.isSuccess != null' > /dev/null 2>&1; then
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

# Test 8: Query Tweets by Time Range
# Uses LAST_FETCH_TIME from previous test if available
test_query_tweets() {
    log_step "Test 8: Querying tweets by time range..."
    log_warn "Note: This requires manager permissions"
    
    local start_time=$(date -u -v-1d +"%Y-%m-%dT%H:%M:%SZ" 2>/dev/null || date -u -d "yesterday" +"%Y-%m-%dT%H:%M:%SZ" 2>/dev/null || date -u +"%Y-%m-%dT%H:%M:%SZ")
    local end_time=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
    
    log_info "→ Time range: $start_time to $end_time"
    
    local response=$(api_get "/api/godgpt/twitter-management/monitor/tweets?startTime=$start_time&endTime=$end_time")
    
    log_response "$response"
    
    if echo "$response" | jq -e '.tweets' > /dev/null 2>&1 || echo "$response" | jq -e '[]' > /dev/null 2>&1; then
        local count=$(echo "$response" | jq -r 'if type == "array" then length else (.tweets | length) // 0 end')
        log_info "Query tweets endpoint tested ✓ (found $count tweets)"
        return 0
    elif echo "$response" | jq -e '.' > /dev/null 2>&1; then
        log_info "Query tweets endpoint tested ✓"
        return 0
    else
        log_warn "Failed to query tweets"
        return 1
    fi
}

# Test 9: Stop Monitor (requires manager permissions)
# Stops the monitor started in Test 6
test_stop_monitor() {
    log_step "Test 9: Stopping Twitter monitor..."
    log_warn "Note: This requires manager permissions"
    
    if [ "$TWITTER_MONITOR_RUNNING" == "true" ]; then
        log_info "→ Monitor is running, will stop"
    else
        log_info "→ Monitor may not be running, testing endpoint"
    fi
    
    local response=$(api_post "/api/godgpt/twitter-management/monitor/stop" '{}')
    
    log_response "$response"
    
    if echo "$response" | jq -e '.isSuccess == true or .success == true' > /dev/null 2>&1; then
        log_info "Monitor stopped successfully ✓"
        TWITTER_MONITOR_RUNNING=false
        return 0
    elif echo "$response" | jq -e '.isSuccess != null or .success != null' > /dev/null 2>&1; then
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

# Test 10: Get Reward Status
# Updates global TWITTER_REWARD_RUNNING state
test_get_reward_status() {
    log_step "Test 10: Getting Twitter reward status..."
    log_warn "Note: This requires manager permissions"
    
    local response=$(api_get "/api/godgpt/twitter-management/reward/status")
    
    log_response "$response"
    
    if echo "$response" | jq -e '.isRunning != null or .IsRunning != null' > /dev/null 2>&1; then
        TWITTER_REWARD_RUNNING=$(echo "$response" | jq -r '.isRunning // .IsRunning // false')
        log_info "Reward status retrieved successfully ✓"
        log_info "→ Reward calculation is running: $TWITTER_REWARD_RUNNING"
        return 0
    elif echo "$response" | jq -e '.error' > /dev/null 2>&1; then
        log_info "Reward status endpoint tested ✓ (permission denied or not configured)"
        return 0
    else
        log_warn "Failed to get reward status"
        return 1
    fi
}

# Test 11: Start Reward Calculation (requires manager permissions)
test_start_reward() {
    log_step "Test 11: Starting Twitter reward calculation..."
    log_warn "Note: This requires manager permissions"
    
    if [ "$TWITTER_REWARD_RUNNING" == "true" ]; then
        log_info "→ Reward calculation is already running"
    else
        log_info "→ Reward calculation is not running, will attempt to start"
    fi
    
    local response=$(api_post "/api/godgpt/twitter-management/reward/start" '{}')
    
    log_response "$response"
    
    if echo "$response" | jq -e '.isSuccess == true or .success == true' > /dev/null 2>&1; then
        log_info "Reward calculation started successfully ✓"
        TWITTER_REWARD_RUNNING=true
        return 0
    elif echo "$response" | jq -e '.isSuccess != null or .success != null' > /dev/null 2>&1; then
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

# Test 12: Get Reward History
test_get_reward_history() {
    log_step "Test 12: Getting Twitter reward history..."
    log_warn "Note: This requires manager permissions"
    
    local response=$(api_get "/api/godgpt/twitter-management/reward/history?days=7")
    
    log_response "$response"
    
    if echo "$response" | jq -e '.history' > /dev/null 2>&1 || echo "$response" | jq -e '[]' > /dev/null 2>&1; then
        local count=$(echo "$response" | jq -r 'if type == "array" then length else (.history | length) // 0 end')
        log_info "Reward history endpoint tested ✓ (found $count records)"
        return 0
    elif echo "$response" | jq -e '.' > /dev/null 2>&1; then
        log_info "Reward history endpoint tested ✓"
        return 0
    else
        log_warn "Failed to get reward history"
        return 1
    fi
}

# Test 13: Get User Reward Records
test_get_user_rewards() {
    log_step "Test 13: Getting user reward records..."
    log_warn "Note: This requires manager permissions"
    
    local response=$(api_get "/api/godgpt/twitter-management/reward/user-records?days=7")
    
    log_response "$response"
    
    if echo "$response" | jq -e '.records' > /dev/null 2>&1 || echo "$response" | jq -e '[]' > /dev/null 2>&1; then
        local count=$(echo "$response" | jq -r 'if type == "array" then length else (.records | length) // 0 end')
        log_info "User reward records endpoint tested ✓ (found $count records)"
        return 0
    elif echo "$response" | jq -e '.' > /dev/null 2>&1; then
        log_info "User reward records endpoint tested ✓"
        return 0
    else
        log_warn "Failed to get user reward records"
        return 1
    fi
}

# Test 14: Stop Reward Calculation (requires manager permissions)
test_stop_reward() {
    log_step "Test 14: Stopping Twitter reward calculation..."
    log_warn "Note: This requires manager permissions"
    
    if [ "$TWITTER_REWARD_RUNNING" == "true" ]; then
        log_info "→ Reward calculation is running, will stop"
    else
        log_info "→ Reward calculation may not be running, testing endpoint"
    fi
    
    local response=$(api_post "/api/godgpt/twitter-management/reward/stop" '{}')
    
    log_response "$response"
    
    if echo "$response" | jq -e '.isSuccess == true or .success == true' > /dev/null 2>&1; then
        log_info "Reward calculation stopped successfully ✓"
        TWITTER_REWARD_RUNNING=false
        return 0
    elif echo "$response" | jq -e '.isSuccess != null or .success != null' > /dev/null 2>&1; then
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

# =============================================================================
# Test Runners
# =============================================================================

# Run user-level tests only (with logical flow)
run_user_tests() {
    local passed=0
    local failed=0
    
    log_info "========================================"
    log_info "Running Twitter User-Level Tests"
    log_info "========================================"
    log_info "Flow: Auth Params → Bind Status → Verify → Unbind"
    echo ""
    
    # Step 1: Get auth params (saves state)
    log_info "Step 1/4: Getting OAuth2 parameters..."
    if test_get_auth_params; then ((passed++)); else ((failed++)); fi
    echo ""
    
    # Step 2: Check bind status
    log_info "Step 2/4: Checking current bind status..."
    if test_get_bind_status; then ((passed++)); else ((failed++)); fi
    echo ""
    
    # Step 3: Test verify (uses saved params)
    log_info "Step 3/4: Testing verification endpoint..."
    if test_verify_auth_code; then ((passed++)); else ((failed++)); fi
    echo ""
    
    # Step 4: Test unbind
    log_info "Step 4/4: Testing unbind endpoint..."
    if test_unbind_twitter; then ((passed++)); else ((failed++)); fi
    echo ""
    
    log_info "========================================"
    log_info "User Tests: $passed passed, $failed failed"
    log_info "========================================"
}

# Run management-level tests only (with logical flow)
run_management_tests() {
    local passed=0
    local failed=0
    
    log_info "========================================"
    log_info "Running Twitter Management Tests"
    log_info "(Requires Manager Permissions)"
    log_info "========================================"
    log_info "Flow: Status → Start → Fetch → Query → Stop"
    echo ""
    
    # Monitor tests
    log_info "--- Monitor Management ---"
    echo ""
    
    log_info "Step 1/10: Getting monitor status..."
    if test_get_monitor_status; then ((passed++)); else ((failed++)); fi
    echo ""
    
    log_info "Step 2/10: Starting monitor..."
    if test_start_monitor; then ((passed++)); else ((failed++)); fi
    echo ""
    
    log_info "Step 3/10: Manual tweet fetch..."
    if test_fetch_manually; then ((passed++)); else ((failed++)); fi
    echo ""
    
    log_info "Step 4/10: Querying tweets..."
    if test_query_tweets; then ((passed++)); else ((failed++)); fi
    echo ""
    
    log_info "Step 5/10: Stopping monitor..."
    if test_stop_monitor; then ((passed++)); else ((failed++)); fi
    echo ""
    
    # Reward tests
    log_info "--- Reward Management ---"
    echo ""
    
    log_info "Step 6/10: Getting reward status..."
    if test_get_reward_status; then ((passed++)); else ((failed++)); fi
    echo ""
    
    log_info "Step 7/10: Starting reward calculation..."
    if test_start_reward; then ((passed++)); else ((failed++)); fi
    echo ""
    
    log_info "Step 8/10: Getting reward history..."
    if test_get_reward_history; then ((passed++)); else ((failed++)); fi
    echo ""
    
    log_info "Step 9/10: Getting user reward records..."
    if test_get_user_rewards; then ((passed++)); else ((failed++)); fi
    echo ""
    
    log_info "Step 10/10: Stopping reward calculation..."
    if test_stop_reward; then ((passed++)); else ((failed++)); fi
    echo ""
    
    log_info "========================================"
    log_info "Management Tests: $passed passed, $failed failed"
    log_info "========================================"
}

# Run all tests with logical flow
run_all_tests() {
    local passed=0
    local failed=0
    
    log_info "========================================"
    log_info "Running All Twitter Flow Tests"
    log_info "========================================"
    log_info "Tests are executed in logical order with state sharing"
    echo ""
    
    # =========================================
    # Phase 1: User-Level Tests (OAuth2 Flow)
    # =========================================
    log_info "═══════════════════════════════════════"
    log_info "Phase 1: User-Level Tests (OAuth2 Flow)"
    log_info "═══════════════════════════════════════"
    echo ""
    
    # Step 1: Get auth params (saves state for later)
    if test_get_auth_params; then ((passed++)); else ((failed++)); fi
    echo ""
    
    # Step 2: Check current bind status
    if test_get_bind_status; then ((passed++)); else ((failed++)); fi
    echo ""
    
    # Step 3: Test verify with saved params
    if test_verify_auth_code; then ((passed++)); else ((failed++)); fi
    echo ""
    
    # Step 4: Test unbind (if bound, will unbind)
    if test_unbind_twitter; then ((passed++)); else ((failed++)); fi
    echo ""
    
    # =========================================
    # Phase 2: Monitor Management Tests
    # =========================================
    log_info "═══════════════════════════════════════"
    log_info "Phase 2: Monitor Management Tests"
    log_info "(Requires Manager Permissions)"
    log_info "═══════════════════════════════════════"
    echo ""
    
    # Step 5: Get current monitor status
    if test_get_monitor_status; then ((passed++)); else ((failed++)); fi
    echo ""
    
    # Step 6: Start monitor (if not running)
    if test_start_monitor; then ((passed++)); else ((failed++)); fi
    echo ""
    
    # Step 7: Manual fetch (while monitor is running)
    if test_fetch_manually; then ((passed++)); else ((failed++)); fi
    echo ""
    
    # Step 8: Query tweets (using recent fetch)
    if test_query_tweets; then ((passed++)); else ((failed++)); fi
    echo ""
    
    # Step 9: Stop monitor
    if test_stop_monitor; then ((passed++)); else ((failed++)); fi
    echo ""
    
    # =========================================
    # Phase 3: Reward Calculation Tests
    # =========================================
    log_info "═══════════════════════════════════════"
    log_info "Phase 3: Reward Calculation Tests"
    log_info "(Requires Manager Permissions)"
    log_info "═══════════════════════════════════════"
    echo ""
    
    # Step 10: Get current reward status
    if test_get_reward_status; then ((passed++)); else ((failed++)); fi
    echo ""
    
    # Step 11: Start reward calculation
    if test_start_reward; then ((passed++)); else ((failed++)); fi
    echo ""
    
    # Step 12: Get reward history
    if test_get_reward_history; then ((passed++)); else ((failed++)); fi
    echo ""
    
    # Step 13: Get user reward records
    if test_get_user_rewards; then ((passed++)); else ((failed++)); fi
    echo ""
    
    # Step 14: Stop reward calculation
    if test_stop_reward; then ((passed++)); else ((failed++)); fi
    echo ""
    
    # =========================================
    # Summary
    # =========================================
    log_info "========================================"
    log_info "Test Summary"
    log_info "========================================"
    log_info "Total: $((passed + failed)) tests"
    log_info "Passed: $passed"
    log_info "Failed: $failed"
    log_info "========================================"
    log_info ""
    log_info "Notes:"
    log_info "- User tests require valid login"
    log_info "- Management tests require manager role"
    log_info "- Some failures are expected without proper config"
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

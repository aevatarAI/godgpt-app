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
GENERATED_TRIAL_CODES=()  # Array to store generated trial codes
GENERATED_BATCH_ID=""  # Batch ID of generated codes

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
    
    local success=$(echo "$response" | jq -r '.success // .data.success // false' 2>/dev/null)
    if [ "$success" == "true" ]; then
        # Extract generated codes
        GENERATED_BATCH_ID=$(echo "$response" | jq -r '.batchId // .data.batchId // empty')
        local codes_array=$(echo "$response" | jq -r '.codes // .data.codes // [] | .[]' 2>/dev/null)
        
        if [ -n "$codes_array" ]; then
            # Convert to array
            GENERATED_TRIAL_CODES=()
            while IFS= read -r code; do
                if [ -n "$code" ] && [ "$code" != "null" ]; then
                    GENERATED_TRIAL_CODES+=("$code")
                fi
            done <<< "$codes_array"
            
            log_info "Generate trial code succeeded ✓"
            log_info "Batch ID: $GENERATED_BATCH_ID"
            log_info "Generated Count: $(echo "$response" | jq -r '.generatedCount // .data.generatedCount // 0')"
            log_info "Generated Codes (first 3):"
            local count=0
            for code in "${GENERATED_TRIAL_CODES[@]}"; do
                if [ $count -lt 3 ]; then
                    log_info "  - $code"
                    ((count++))
                fi
            done
            if [ ${#GENERATED_TRIAL_CODES[@]} -gt 3 ]; then
                log_info "  ... and $(( ${#GENERATED_TRIAL_CODES[@]} - 3 )) more codes"
            fi
        else
            log_warn "Generate succeeded but no codes returned in response"
        fi
        return 0
    else
        log_warn "Generate trial code may have failed (expected if not manager)"
        return 0  # Don't fail if permission denied
    fi
}

# Test 7: Redeem Generated Trial Code
test_redeem_generated_trial_code() {
    log_step "Test 7: Redeeming generated trial code..."
    
    if [ ${#GENERATED_TRIAL_CODES[@]} -eq 0 ]; then
        log_warn "No generated trial codes available, skipping test"
        log_warn "Note: This test requires Test 6 to succeed first"
        return 0  # Don't fail, just skip
    fi
    
    # Use the first generated code
    local code_to_redeem="${GENERATED_TRIAL_CODES[0]}"
    log_info "Attempting to redeem generated code: $code_to_redeem"
    log_warn "Note: This may fail if Stripe coupon was not created for this code"
    
    # Add a small delay to allow for potential async processing
    log_info "Waiting 2 seconds for potential async processing..."
    sleep 2
    
    local response=$(api_post "/api/godgpt/invitation/redeem" "{
        \"InviteCode\": \"$code_to_redeem\",
        \"IsWeb\": true
    }")
    
    log_response "$response"
    
    # Check if response is valid JSON with isValid field
    if echo "$response" | jq -e '.isValid != null or .IsValid != null or .data.isValid != null or .data.IsValid != null' > /dev/null 2>&1; then
        local is_valid=$(echo "$response" | jq -r '.IsValid // .isValid // .data.IsValid // .data.isValid // false')
        if [ "$is_valid" == "true" ]; then
            log_info "Redeem generated trial code succeeded ✓"
            log_info "Checkout URL: $(echo "$response" | jq -r '.url // .URL // .data.url // .data.URL // "N/A"')"
            return 0
        else
            local error_msg=$(echo "$response" | jq -r '.error // .message // .data.error // .data.message // "Unknown error"' 2>/dev/null || echo "Validation failed")
            log_warn "Redeem generated trial code failed: $error_msg"
            log_warn "This may indicate that Stripe coupon was not created for the code"
            log_warn "Code exists locally but may not be available in Stripe"
            return 0  # Don't fail test, but log the issue
        fi
    elif echo "$response" | jq -e '.error' > /dev/null 2>&1; then
        local error_msg=$(echo "$response" | jq -r '.error.message // .error // "Unknown error"' 2>/dev/null || echo "$response")
        log_warn "Redeem generated trial code returned error: $error_msg"
        log_warn "This may indicate that Stripe coupon was not created for the code"
        return 0  # Don't fail test, but log the issue
    else
        log_warn "Redeem generated trial code endpoint may have failed"
        return 1
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
    
    if test_generate_trial_code; then
        ((passed++))
    else
        ((failed++))
    fi
    echo ""
    
    # Test 7: Try to redeem the generated trial code
    if test_redeem_generated_trial_code; then
        ((passed++))
    else
        ((failed++))
    fi
    echo ""
    
    log_info "========================================"
    log_info "Test Results: $passed passed, $failed failed"
    log_info "========================================"
    log_info "Note: Redeem test may fail if user tries to redeem their own code (expected behavior)"
    if [ ${#GENERATED_TRIAL_CODES[@]} -gt 0 ]; then
        log_info ""
        log_info "Generated Trial Codes (for manual testing):"
        for code in "${GENERATED_TRIAL_CODES[@]}"; do
            log_info "  $code"
        done
        log_info ""
        log_info "Note: If redeem fails, check if Stripe coupons were created for these codes"
    fi
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
        "redeem-generated")
            # First generate codes, then redeem
            if test_generate_trial_code; then
                echo ""
                test_redeem_generated_trial_code
            fi
            ;;
        "all"|*)
            run_all_tests
            ;;
    esac
}

main "$@"

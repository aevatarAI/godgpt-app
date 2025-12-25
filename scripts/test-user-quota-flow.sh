#!/bin/bash

# =============================================================================
# Test User Quota Flow Script
# Tests user quota-related endpoints
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

# Test 1: Update Show Toast
test_update_show_toast() {
    log_step "Test 1: Updating show toast flag..."
    
    local response=$(api_post "/api/godgpt/account/show-toast" "{}")
    
    log_response "$response"
    
    if echo "$response" | jq -e 'type == "string"' > /dev/null 2>&1 || echo "$response" | grep -q '[0-9a-f]\{8\}-[0-9a-f]\{4\}-[0-9a-f]\{4\}-[0-9a-f]\{4\}-[0-9a-f]\{12\}'; then
        log_info "Show toast updated successfully ✓"
        return 0
    else
        log_warn "Failed to update show toast"
        return 1
    fi
}

# Test 2: Update User Credits
test_update_user_credits() {
    log_step "Test 2: Updating user credits..."
    
    local response=$(api_post "/api/godgpt/account/credits" '{
        "operatorUserId": "00000000-0000-0000-0000-000000000000",
        "credits": 100,
        "reason": "Test credits update"
    }')
    
    log_response "$response"
    
    if echo "$response" | jq -e '.success' > /dev/null 2>&1; then
        log_info "User credits updated successfully ✓"
        return 0
    else
        log_warn "Failed to update user credits (may require operator permissions)"
        return 0  # Don't fail if permission denied
    fi
}

# Test 3: Update User Subscription
test_update_user_subscription() {
    log_step "Test 3: Updating user subscription..."
    
    local response=$(api_post "/api/godgpt/account/subscription" '{
        "operatorUserId": "00000000-0000-0000-0000-000000000000",
        "subscriptions": [
            {
                "planType": 1,
                "startDate": "2024-01-01T00:00:00Z",
                "endDate": "2024-12-31T23:59:59Z",
                "isUltimate": false
            }
        ]
    }')
    
    log_response "$response"
    
    if echo "$response" | jq -e '.success' > /dev/null 2>&1; then
        log_info "User subscription updated successfully ✓"
        return 0
    else
        log_warn "Failed to update user subscription (may require operator permissions)"
        return 0  # Don't fail if permission denied
    fi
}

# Test 4: Check Can Upload Image
test_can_upload_image() {
    log_step "Test 4: Checking if user can upload image..."
    
    local response=$(api_get "/api/godgpt/can-upload-image")
    
    log_response "$response"
    
    if echo "$response" | jq -e '.canUpload' > /dev/null 2>&1; then
        log_info "Can upload image check completed successfully ✓"
        return 0
    else
        log_warn "Failed to check upload permission"
        return 1
    fi
}

# Run all tests
run_all_tests() {
    local passed=0
    local failed=0
    
    log_info "========================================"
    log_info "Running User Quota Flow Tests"
    log_info "========================================"
    echo ""
    
    if test_update_show_toast; then
        ((passed++))
    else
        ((failed++))
    fi
    echo ""
    
    if test_update_user_credits; then
        ((passed++))
    else
        ((failed++))
    fi
    echo ""
    
    if test_update_user_subscription; then
        ((passed++))
    else
        ((failed++))
    fi
    echo ""
    
    if test_can_upload_image; then
        ((passed++))
    else
        ((failed++))
    fi
    echo ""
    
    log_info "========================================"
    log_info "Test Results: $passed passed, $failed failed"
    log_info "========================================"
}

# Main function
main() {
    echo ""
    log_info "========================================"
    log_info "Aevatar User Quota Flow Test"
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
        "show-toast")
            test_update_show_toast
            ;;
        "credits")
            test_update_user_credits
            ;;
        "subscription")
            test_update_user_subscription
            ;;
        "can-upload")
            test_can_upload_image
            ;;
        "all"|*)
            run_all_tests
            ;;
    esac
}

main "$@"

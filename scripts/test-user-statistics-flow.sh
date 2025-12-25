#!/bin/bash

# =============================================================================
# Test User Statistics Flow Script
# Tests user statistics-related endpoints
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

# Test 1: Record App Rating
test_record_app_rating() {
    log_step "Test 1: Recording app rating..."
    
    local response=$(api_post "/api/godgpt/user-statistics/app-rating" '{
        "platform": "iOS",
        "deviceId": "test_device_12345"
    }')
    
    log_response "$response"
    
    if echo "$response" | jq -e '.ratingCount' > /dev/null 2>&1 || echo "$response" | jq -e '.ratingId' > /dev/null 2>&1 || echo "$response" | jq -e '.success' > /dev/null 2>&1; then
        log_info "App rating recorded successfully ✓"
        return 0
    else
        log_warn "Failed to record app rating"
        return 1
    fi
}

# Test 2: Check Can User Rate App
test_can_user_rate_app() {
    log_step "Test 2: Checking if user can rate app..."
    
    local response=$(api_get "/api/godgpt/user-statistics/can-rate?deviceId=test_device_12345")
    
    log_response "$response"
    
    if echo "$response" | grep -qE '^(true|false)$'; then
        log_info "Can rate app check completed successfully ✓"
        return 0
    else
        log_warn "Failed to check if user can rate app"
        return 1
    fi
}

# Run all tests
run_all_tests() {
    local passed=0
    local failed=0
    
    log_info "========================================"
    log_info "Running User Statistics Flow Tests"
    log_info "========================================"
    echo ""
    
    if test_record_app_rating; then
        ((passed++))
    else
        ((failed++))
    fi
    echo ""
    
    if test_can_user_rate_app; then
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
    log_info "Aevatar User Statistics Flow Test"
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
        "rating")
            test_record_app_rating
            ;;
        "can-rate")
            test_can_user_rate_app
            ;;
        "all"|*)
            run_all_tests
            ;;
    esac
}

main "$@"

#!/bin/bash

# =============================================================================
# Test Content Flow Script
# Tests GodGPTContentController endpoints (requires authentication)
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Load common test utilities
source "$SCRIPT_DIR/test-common.sh"

# Test: Get Today's Awakening Content
test_get_today_awakening() {
    log_step "Test: Getting today's awakening content..."
    
    local response=$(api_get "/api/godgpt/awakening/today" "GodgptLanguage: English")
    
    log_response "$response"
    
    # Response can be null or a valid object
    if [ "$response" == "null" ] || [ -z "$response" ]; then
        log_info "No awakening content for today (this is valid) ✓"
        return 0
    elif echo "$response" | jq -e '.' > /dev/null 2>&1; then
        log_info "Awakening content retrieved successfully ✓"
        return 0
    else
        log_error "Failed to get awakening content"
        return 1
    fi
}

# Test with region parameter
test_get_today_awakening_with_region() {
    log_step "Test: Getting today's awakening content with region..."
    
    local response=$(api_get "/api/godgpt/awakening/today?region=US" "GodgptLanguage: English")
    
    log_response "$response"
    
    if [ "$response" == "null" ] || [ -z "$response" ]; then
        log_info "No awakening content for today with region (this is valid) ✓"
        return 0
    elif echo "$response" | jq -e '.' > /dev/null 2>&1; then
        log_info "Awakening content with region retrieved successfully ✓"
        return 0
    else
        log_error "Failed to get awakening content with region"
        return 1
    fi
}

# Run all tests
run_all_tests() {
    local passed=0
    local failed=0
    
    log_info "========================================"
    log_info "Running Content Flow Tests"
    log_info "========================================"
    echo ""
    
    if ! login; then
        log_error "Login failed, aborting tests"
        exit 1
    fi
    echo ""
    
    if test_get_today_awakening; then
        ((passed++))
    else
        ((failed++))
    fi
    echo ""
    
    if test_get_today_awakening_with_region; then
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
    log_info "GodGPT Content Flow Test"
    log_info "========================================"
    echo ""
    
    case "${1:-all}" in
        "all"|*)
            run_all_tests
            ;;
    esac
}

main "$@"


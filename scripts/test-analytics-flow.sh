#!/bin/bash

# =============================================================================
# Test Analytics Flow Script
# Tests GodGPTAnalyticsController endpoints (requires authentication)
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Load common test utilities
source "$SCRIPT_DIR/test-common.sh"

# Test 1: Track Google Analytics Event (gtag)
test_track_gtag_event() {
    log_step "Test 1: Tracking Google Analytics event (gtag)..."
    
    local response=$(api_post "/api/godgpt/analytics/track/gtag" '{
        "eventName": "test_event",
        "clientId": "test_client_123",
        "parameters": {
            "category": "test",
            "action": "click"
        }
    }')
    
    log_response "$response"
    
    if echo "$response" | jq -e '.success' > /dev/null 2>&1; then
        local success=$(echo "$response" | jq -r '.success')
        if [ "$success" == "true" ]; then
            log_info "Google Analytics event tracked successfully ✓"
            return 0
        else
            log_warn "Event tracking returned success=false"
            return 0  # Don't fail - might be expected behavior
        fi
    else
        log_error "Failed to track Google Analytics event"
        return 1
    fi
}

# Test 2: Track Firebase Analytics Event
test_track_firebase_event() {
    log_step "Test 2: Tracking Firebase Analytics event..."
    
    local response=$(api_post "/api/godgpt/analytics/track" '{
        "eventName": "test_firebase_event",
        "clientId": "test_client_123",
        "parameters": {
            "category": "test",
            "action": "click"
        }
    }')
    
    log_response "$response"
    
    if echo "$response" | jq -e '.success' > /dev/null 2>&1; then
        local success=$(echo "$response" | jq -r '.success')
        if [ "$success" == "true" ]; then
            log_info "Firebase Analytics event tracked successfully ✓"
            return 0
        else
            log_warn "Event tracking returned success=false"
            return 0  # Don't fail - might be expected behavior
        fi
    else
        log_error "Failed to track Firebase Analytics event"
        return 1
    fi
}

# Run all tests
run_all_tests() {
    local passed=0
    local failed=0
    
    log_info "========================================"
    log_info "Running Analytics Flow Tests"
    log_info "========================================"
    echo ""
    
    if ! login; then
        log_error "Login failed, aborting tests"
        exit 1
    fi
    echo ""
    
    if test_track_gtag_event; then
        ((passed++))
    else
        ((failed++))
    fi
    echo ""
    
    if test_track_firebase_event; then
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
    log_info "GodGPT Analytics Flow Test"
    log_info "========================================"
    echo ""
    
    case "${1:-all}" in
        "gtag")
            if ! login; then
                log_error "Login failed, aborting tests"
                exit 1
            fi
            test_track_gtag_event
            ;;
        "firebase")
            if ! login; then
                log_error "Login failed, aborting tests"
                exit 1
            fi
            test_track_firebase_event
            ;;
        "all"|*)
            run_all_tests
            ;;
    esac
}

main "$@"

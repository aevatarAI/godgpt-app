#!/bin/bash

# =============================================================================
# Test AppConfig Flow Script
# Tests GodGPTAppConfigController endpoints (anonymous access)
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Load common test utilities
source "$SCRIPT_DIR/test-common.sh"

# Test 1: Query Version
test_query_version() {
    log_step "Test 1: Querying GodGPT version..."
    
    local response=$(api_get_guest "/api/godgpt/query-version")
    
    log_response "$response"
    
    if [ -n "$response" ] && [ ${#response} -gt 0 ]; then
        log_info "Version retrieved successfully ✓"
        log_info "Version: $response"
        return 0
    else
        log_error "Failed to get version"
        return 1
    fi
}

# Test 2: Query Config
test_query_config() {
    log_step "Test 2: Querying GodGPT configuration..."
    
    local response=$(api_get_guest "/api/godgpt/query-config")
    
    log_response "$response"
    
    if echo "$response" | jq -e '.version' > /dev/null 2>&1; then
        local version=$(echo "$response" | jq -r '.version')
        log_info "Configuration retrieved successfully ✓"
        log_info "Version: $version"
        
        if echo "$response" | jq -e '.features' > /dev/null 2>&1; then
            log_info "Features configuration found ✓"
        fi
        
        return 0
    else
        log_error "Failed to get configuration"
        return 1
    fi
}

# Run all tests
run_all_tests() {
    local passed=0
    local failed=0
    
    log_info "========================================"
    log_info "Running AppConfig Flow Tests"
    log_info "========================================"
    echo ""
    
    if test_query_version; then
        ((passed++))
    else
        ((failed++))
    fi
    echo ""
    
    if test_query_config; then
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
    log_info "GodGPT AppConfig Flow Test"
    log_info "========================================"
    echo ""
    
    check_services
    echo ""
    
    case "${1:-all}" in
        "version")
            test_query_version
            ;;
        "config")
            test_query_config
            ;;
        "all"|*)
            run_all_tests
            ;;
    esac
}

main "$@"

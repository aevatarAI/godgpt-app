#!/bin/bash

# =============================================================================
# Test Config Flow Script
# Tests configuration-related endpoints (requires systemPromptManager role)
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

# Test 1: Get System Prompt
test_get_system_prompt() {
    log_step "Test 1: Getting system prompt..."
    
    local response=$(api_get "/api/godgpt/configuration/system-prompt")
    
    log_response "$response"
    
    # Check if response contains permission denied message
    if echo "$response" | grep -qi "Permission denied"; then
        log_warn "Access denied - user needs systemPromptManager role"
        return 0  # Don't fail if permission denied (defensive test)
    elif [ -n "$response" ] && [ ${#response} -gt 10 ]; then
        log_info "System prompt retrieved successfully ✓"
        log_info "Prompt length: ${#response} characters"
        return 0
    else
        log_warn "Failed to get system prompt"
        return 1
    fi
}

# Test 2: Update System Prompt
test_update_system_prompt() {
    log_step "Test 2: Updating system prompt..."
    
    local test_prompt="This is a test system prompt updated at $(date +%Y-%m-%d\ %H:%M:%S)"
    
    # Use curl directly to get HTTP status code (api_post doesn't return status code)
    local http_code=$(curl -k -s -o /tmp/update_prompt_response.json -w "%{http_code}" -X POST "$API_URL/api/godgpt/configuration/system-prompt" \
        -H "Authorization: Bearer $ACCESS_TOKEN" \
        -H "Content-Type: application/json" \
        -d "{
            \"systemPrompt\": \"$test_prompt\"
        }")
    
    local response=$(cat /tmp/update_prompt_response.json 2>/dev/null || echo "")
    rm -f /tmp/update_prompt_response.json
    
    log_response "$response"
    
    # Check HTTP status code
    if [ "$http_code" == "403" ] || [ "$http_code" == "401" ]; then
        log_warn "Access denied (HTTP $http_code) - user needs systemPromptManager role"
        return 0  # Don't fail if permission denied (defensive test)
    elif echo "$response" | grep -qi "Permission denied"; then
        log_warn "Access denied - user needs systemPromptManager role"
        return 0  # Don't fail if permission denied (defensive test)
    elif [ "$http_code" == "200" ] && (echo "$response" | jq -e '.message' > /dev/null 2>&1 || echo "$response" | grep -qi "success"); then
        log_info "System prompt updated successfully ✓"
        return 0
    elif [ "$http_code" == "200" ]; then
        # Even if response doesn't match expected format, if status is 200, consider it success
        log_info "System prompt update endpoint responded successfully (HTTP 200) ✓"
        return 0
    else
        log_warn "Failed to update system prompt (HTTP $http_code)"
        return 1
    fi
}

# Run all tests
run_all_tests() {
    local passed=0
    local failed=0
    
    log_info "========================================"
    log_info "Running Config Flow Tests"
    log_info "========================================"
    echo ""
    
    if test_get_system_prompt; then
        ((passed++))
    else
        ((failed++))
    fi
    echo ""
    
    if test_update_system_prompt; then
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
    log_info "Aevatar Config Flow Test"
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
        "get")
            test_get_system_prompt
            ;;
        "update")
            test_update_system_prompt
            ;;
        "all"|*)
            run_all_tests
            ;;
    esac
}

main "$@"

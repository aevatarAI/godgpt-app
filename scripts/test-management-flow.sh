#!/bin/bash

# =============================================================================
# Test Management Flow Script
# Tests management-related endpoints (requires manager permissions)
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

# Test 0: Create a batch (generate trial codes)
test_create_batch() {
    log_step "Test 0: Creating a test batch (generating trial codes)..."
    log_warn "Note: This requires manager permissions"
    
    # Calculate start and end times (30 days from now)
    local start_time=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
    local end_time=$(date -u -v+30d +"%Y-%m-%dT%H:%M:%SZ" 2>/dev/null || date -u -d "+30 days" +"%Y-%m-%dT%H:%M:%SZ" 2>/dev/null || date -u +"%Y-%m-%dT%H:%M:%SZ")
    
    local response=$(api_post "/api/godgpt/invitation/generate-trial-code" "{
        \"trialDays\": 30,
        \"productId\": \"test_product\",
        \"platform\": 0,
        \"startTime\": \"$start_time\",
        \"endTime\": \"$end_time\",
        \"quantity\": 5,
        \"description\": \"Test batch for management flow\"
    }")
    
    log_response "$response"
    
    if echo "$response" | jq -e '.success == true' > /dev/null 2>&1; then
        local batch_id=$(echo "$response" | jq -r '.batchId')
        if [ "$batch_id" != "null" ] && [ -n "$batch_id" ]; then
            log_info "Batch created successfully ✓"
            log_info "Batch ID: $batch_id"
            log_info "Generated Count: $(echo "$response" | jq -r '.generatedCount')"
            echo "$batch_id"  # Return batchId to stdout for next test
            return 0
        fi
    fi
    
    # If creation failed, return empty (caller will handle fallback)
    local error_msg=$(echo "$response" | jq -r '.error.message // .message // empty' 2>/dev/null || echo "$response")
    log_warn "Failed to create batch: $error_msg"
    echo ""  # Return empty string to stdout
    return 1
}

# Test 1: Get Batch Info
test_get_batch_info() {
    log_step "Test 1: Getting batch info..."
    log_warn "Note: This requires manager permissions"
    
    local batch_id="${1:-}"
    
    if [ -z "$batch_id" ]; then
        log_error "Batch ID is required"
        return 1
    fi
    
    local response=$(api_get "/api/godgpt/management/batch-info/$batch_id")
    
    log_response "$response"
    
    if echo "$response" | jq -e '.batchId' > /dev/null 2>&1; then
        log_info "Batch info retrieved successfully ✓"
        log_info "Batch ID: $(echo "$response" | jq -r '.batchId')"
        log_info "Total Generated: $(echo "$response" | jq -r '.totalGenerated')"
        log_info "Used Count: $(echo "$response" | jq -r '.usedCount')"
        log_info "Status: $(echo "$response" | jq -r '.status')"
        return 0
    else
        # Check if it's a permission error
        local error_msg=$(echo "$response" | jq -r '.error.message // .message // empty' 2>/dev/null || echo "$response")
        if [[ "$error_msg" == *"manager"* ]] || [[ "$error_msg" == *"Permission"* ]] || [[ "$error_msg" == *"Security"* ]]; then
            log_warn "Access denied (expected if not manager): $error_msg"
        else
            log_warn "Failed to get batch info: $error_msg"
        fi
        return 1
    fi
}

# Run all tests
run_all_tests() {
    local passed=0
    local failed=0
    
    log_info "========================================"
    log_info "Running Management Flow Tests"
    log_info "========================================"
    log_warn "Note: These tests require manager permissions"
    echo ""
    
    # Step 1: Create a batch first
    local batch_id=""
    local temp_file=$(mktemp)
    # Capture both stdout (batchId) and stderr (logs) separately
    test_create_batch > "$temp_file" 2>&1 || true  # Don't fail if creation fails
    # Extract batchId from the response JSON (look for numeric batchId in JSON)
    batch_id=$(cat "$temp_file" | jq -r '.batchId // empty' 2>/dev/null || \
               cat "$temp_file" | grep -oE '"batchId"[[:space:]]*:[[:space:]]*[0-9]+' | grep -oE '[0-9]+' | head -1 || \
               cat "$temp_file" | grep -E '^[0-9]+$' | head -1)
    rm -f "$temp_file"
    
    if [ -n "$batch_id" ] && [ "$batch_id" != "null" ] && [ "$batch_id" != "" ]; then
        log_info "Using created batchId: $batch_id"
        ((passed++))
    else
        log_warn "Failed to create batch (may require special permissions)"
        log_warn "Will try to use a test batchId instead"
        # Use a recent timestamp as fallback
        batch_id=$(date +%s)
        log_info "Using fallback batchId: $batch_id"
        ((failed++))
    fi
    echo ""
    
    # Step 2: Get batch info using the created batchId
    if [ -n "$batch_id" ] && test_get_batch_info "$batch_id"; then
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
    log_info "Aevatar Management Flow Test"
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
        "create-batch")
            test_create_batch
            ;;
        "batch-info")
            test_get_batch_info "${2:-}"
            ;;
        "all"|*)
            run_all_tests
            ;;
    esac
}

main "$@"

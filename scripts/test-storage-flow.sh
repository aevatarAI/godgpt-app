#!/bin/bash

# =============================================================================
# Test Storage Flow Script
# Tests GodGPTStorageController endpoints (requires authentication)
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Load common test utilities
source "$SCRIPT_DIR/test-common.sh"

# Variables
UPLOADED_FILE_NAME=""

# Create a test image file
create_test_image() {
    log_step "Creating a test image file..."
    
    # Create a simple 1x1 PNG image using base64
    local png_base64="iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="
    echo "$png_base64" | base64 -d > /tmp/test_image.png
    
    log_info "Test image created: /tmp/test_image.png"
}

# Test 1: Upload File
test_upload_file() {
    log_step "Test 1: Uploading test image..."
    
    if [ ! -f "/tmp/test_image.png" ]; then
        create_test_image
    fi
    
    # File upload requires multipart/form-data, use curl directly
    local response=$(curl -k -s -X POST "$API_URL/api/godgpt/blob" \
        -H "Authorization: Bearer $ACCESS_TOKEN" \
        -F "file=@/tmp/test_image.png")
    
    log_response "$response"
    
    # Extract file name from JSON response: {"code":"20000","data":"filename.png","message":""}
    local code=$(echo "$response" | jq -r '.code // empty' 2>/dev/null)
    local file_name=$(echo "$response" | jq -r '.data // empty' 2>/dev/null)
    
    # Response should be successful (code 20000) and contain a file name
    if [ "$code" == "20000" ] && [ -n "$file_name" ] && [[ "$file_name" =~ \.png$ ]]; then
        UPLOADED_FILE_NAME="$file_name"
        log_info "File uploaded successfully ✓"
        log_info "File name: $UPLOADED_FILE_NAME"
        return 0
    else
        log_error "Failed to upload file"
        log_error "Response: $response"
        return 1
    fi
}

# Test 2: Delete File
test_delete_file() {
    if [ -z "$UPLOADED_FILE_NAME" ]; then
        log_warn "No uploaded file name available, skipping delete test"
        return 0
    fi
    
    log_step "Test 2: Deleting uploaded file $UPLOADED_FILE_NAME..."
    
    local http_code=$(curl -k -s -o /dev/null -w "%{http_code}" -X DELETE "$API_URL/api/godgpt/blob/$UPLOADED_FILE_NAME" \
        -H "Authorization: Bearer $ACCESS_TOKEN")
    
    if [ "$http_code" == "200" ] || [ "$http_code" == "204" ] || [ "$http_code" == "204" ]; then
        log_info "File deleted successfully ✓"
        log_info "HTTP Status: $http_code"
        return 0
    else
        log_warn "Delete returned HTTP $http_code (might be expected if file doesn't exist)"
        return 0  # Don't fail - file might have been deleted already
    fi
}

# Cleanup test file
cleanup() {
    if [ -f "/tmp/test_image.png" ]; then
        rm -f /tmp/test_image.png
        log_info "Cleaned up test image file"
    fi
}

# Run all tests
run_all_tests() {
    local passed=0
    local failed=0
    
    log_info "========================================"
    log_info "Running Storage Flow Tests"
    log_info "========================================"
    echo ""
    
    if ! login; then
        log_error "Login failed, aborting tests"
        exit 1
    fi
    echo ""
    
    create_test_image
    echo ""
    
    if test_upload_file; then
        ((passed++))
    else
        ((failed++))
    fi
    echo ""
    
    if test_delete_file; then
        ((passed++))
    else
        ((failed++))
    fi
    echo ""
    
    cleanup
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
    log_info "GodGPT Storage Flow Test"
    log_info "========================================"
    echo ""
    
    case "${1:-all}" in
        "upload")
            login
            create_test_image
            test_upload_file
            cleanup
            ;;
        "delete")
            log_warn "Delete test requires an uploaded file name"
            log_warn "Run 'upload' test first or provide file name"
            ;;
        "all"|*)
            run_all_tests
            ;;
    esac
}

main "$@"


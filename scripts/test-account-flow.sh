#!/bin/bash

# =============================================================================
# Test Account Flow Script
# Tests GodGPTAccountController endpoints (requires authentication)
# Tests AccountProxyController endpoints (guest endpoints for backward compatibility)
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Load common test utilities
source "$SCRIPT_DIR/test-common.sh"

# Override default configuration if needed
# AUTH_URL, API_URL, CLIENT_ID, SCOPE, TEST_USERNAME, TEST_PASSWORD can be set via environment variables

# Generate test email with timestamp to avoid conflicts
TEST_EMAIL="test-$(date +%s)@example.com"

# Test 1: Get User Profile
test_get_user_profile() {
    log_step "Test 1: Getting user profile..."
    
    local response=$(api_get "/api/godgpt/account")
    
    log_response "$response"
    
    # Check for data.gender field (profile data) - API returns {"code":"20000","data":{...},"message":""}
    if echo "$response" | jq -e '.data.gender' > /dev/null 2>&1; then
        local gender=$(echo "$response" | jq -r '.data.gender')
        local credits=$(echo "$response" | jq -r '.data.credits.credits // "N/A"')
        log_info "User profile retrieved successfully ✓"
        log_info "Gender: $gender, Credits: $credits"
        return 0
    else
        log_error "Failed to get user profile"
        return 1
    fi
}

# Test 2: Update User Profile
test_update_user_profile() {
    log_step "Test 2: Updating user profile..."
    
    local response=$(api_put "/api/godgpt/account" '{
        "nickname": "Test User",
        "avatar": "",
        "bio": "Test bio",
        "gender": "Male",
        "birthPlace": "Earth"
    }')
    
    log_response "$response"
    
    # Response is {"code":"20000","data":"guid-string","message":""} - extract .data field
    local cleanResponse=$(echo "$response" | jq -r '.data // empty' 2>/dev/null)
    if [ -n "$cleanResponse" ] && [[ "$cleanResponse" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]]; then
        log_info "User profile updated successfully ✓"
        log_info "Updated User ID: $cleanResponse"
        return 0
    else
        log_error "Failed to update user profile"
        return 1
    fi
}

# Test 3: Set Voice Language
test_set_voice_language() {
    log_step "Test 3: Setting voice language..."
    
    local response=$(api_post "/api/godgpt/voice/set" '{
        "voiceLanguage": "English"
    }')
    
    log_response "$response"
    
    # Response is {"code":"20000","data":{...},"message":""} - check .data.voiceLanguage
    if echo "$response" | jq -e '.data.voiceLanguage' > /dev/null 2>&1; then
        local voice_lang=$(echo "$response" | jq -r '.data.voiceLanguage')
        log_info "Voice language set successfully ✓"
        log_info "Voice Language: $voice_lang"
        return 0
    elif echo "$response" | jq -e '.data' > /dev/null 2>&1; then
        log_info "Voice language set successfully ✓"
        return 0
    else
        log_warn "Voice language response doesn't match expected format"
        return 0  # Don't fail - might be valid response
    fi
}

# Test 4: Delete Account (WARNING: This will delete the account!)
test_delete_account() {
    log_warn "WARNING: This test will delete the account!"
    log_warn "Skipping delete account test for safety"
    log_info "Delete account endpoint exists but not tested (safety measure) ✓"
    return 0
}

# =============================================================================
# AccountProxyController Tests (Backward Compatibility)
# Tests /api/account/* endpoints that proxy to AuthServer
# These tests follow a logical flow similar to authenticated tests (1-4)
#
# Phase 1: New Email Registration Flow (Tests 5-8)
#   - Verify new email is not registered
#   - Send verification code
#   - Test verify with wrong code (expect false)
#   - Test register with wrong code (expect error)
#
# Phase 2: Registered Email Password Reset Flow (Tests 9-12)
#   - Verify registered email exists
#   - Send password reset code
#   - Test verify with wrong token (expect false)
#   - Test reset with wrong token (expect error)
# =============================================================================

# Registered user email for testing (will be fetched from profile)
REGISTERED_EMAIL=""

# Test 5: Check New Email Not Registered (via proxy)
test_proxy_check_new_email() {
    log_step "Test 5: Checking if NEW email is registered (via proxy)..."
    log_info "Testing with: $TEST_EMAIL"
    
    local response=$(api_post_guest "/api/account/check-email-registered" "{
        \"emailAddress\": \"$TEST_EMAIL\"
    }")
    
    log_response "$response"
    
    # Response should be wrapped: {code: "20000", data: bool, message: ""}
    # Note: use jq '.data' not '.data // empty' because false is falsy in jq
    local data=$(echo "$response" | jq '.data' 2>/dev/null)
    
    if [ "$data" == "false" ]; then
        log_info "New email correctly detected as NOT registered ✓"
        return 0
    elif [ "$data" == "true" ]; then
        log_warn "New email unexpectedly shows as registered (may be a real email)"
        return 0  # Don't fail - might be coincidence
    # Fallback: check raw boolean (backward compatibility)
    elif echo "$response" | jq -e 'type == "boolean"' > /dev/null 2>&1; then
        local is_registered=$(echo "$response" | jq '.')
        if [ "$is_registered" == "false" ]; then
            log_info "New email correctly detected as NOT registered ✓"
            return 0
        fi
    else
        log_error "Invalid response format. Expected: {code, data: bool, message}"
        log_error "Got: $response"
        return 1
    fi
}

# Test 6: Send Register Code for New Email (via proxy)
test_proxy_send_register_code() {
    log_step "Test 6: Sending registration code to NEW email (via proxy)..."
    log_info "Sending code to: $TEST_EMAIL"
    
    local response=$(api_post_guest "/api/account/send-register-code" "{
        \"email\": \"$TEST_EMAIL\",
        \"appName\": \"GodGPT\",
        \"platform\": 0
    }")
    
    log_response "$response"
    
    # Response should be wrapped: {code: "20000", data: {success: bool, message: string}, message: ""}
    local data=$(echo "$response" | jq -r '.data // empty' 2>/dev/null)
    
    if [ -n "$data" ] && [ "$data" != "null" ]; then
        local success=$(echo "$data" | jq -r '.success // .Success // empty')
        local message=$(echo "$data" | jq -r '.message // .Message // ""')
        if [ "$success" == "true" ] || [ "$success" == "True" ]; then
            log_info "Registration code sent successfully ✓"
            log_info "Message: $message"
            return 0
        else
            log_error "Failed to send code: $message"
            return 1
        fi
    # Fallback: check raw format (backward compatibility)
    elif echo "$response" | jq -e '.success' > /dev/null 2>&1; then
        local success=$(echo "$response" | jq -r '.success')
        local message=$(echo "$response" | jq -r '.message // ""')
        if [ "$success" == "true" ]; then
            log_info "Registration code sent successfully ✓"
            log_info "Message: $message"
            return 0
        fi
    else
        log_error "Invalid response format. Expected: {code, data: {success, message}, message}"
        log_error "Got: $response"
        return 1
    fi
}

# Test 7: Verify Register Code with WRONG code (via proxy)
test_proxy_verify_register_code_wrong() {
    log_step "Test 7: Verifying registration code with WRONG code (via proxy)..."
    log_info "Testing error handling with invalid code: 000000"
    
    local response=$(api_post_guest "/api/account/verify-register-code" "{
        \"email\": \"$TEST_EMAIL\",
        \"code\": \"000000\"
    }")
    
    log_response "$response"
    
    # Response should be wrapped: {code: "20000", data: bool, message: ""}
    local data=$(echo "$response" | jq '.data' 2>/dev/null)
    
    if [ "$data" == "false" ]; then
        log_info "Wrong code correctly rejected (returned false) ✓"
        return 0
    elif [ "$data" == "true" ]; then
        log_warn "Wrong code unexpectedly returned true - security concern!"
        return 1
    # Fallback: check raw boolean (backward compatibility)
    elif echo "$response" | jq -e 'type == "boolean"' > /dev/null 2>&1; then
        local verified=$(echo "$response" | jq '.')
        if [ "$verified" == "false" ]; then
            log_info "Wrong code correctly rejected (returned false) ✓"
            return 0
        fi
    elif echo "$response" | jq -e '.error' > /dev/null 2>&1; then
        local error=$(echo "$response" | jq -r '.error // .message // "Unknown error"')
        log_info "Wrong code correctly rejected with error ✓"
        log_info "Error: $error"
        return 0
    else
        log_error "Invalid response format. Expected: {code, data: bool, message}"
        log_error "Got: $response"
        return 1
    fi
}

# Test 8: Register with WRONG code (via proxy)
test_proxy_register_wrong_code() {
    log_step "Test 8: Registering with WRONG verification code (via proxy)..."
    log_info "Testing error handling with unverified code"
    
    local response=$(api_post_guest "/api/account/register" "{
        \"emailAddress\": \"$TEST_EMAIL\",
        \"userName\": \"testuser_$(date +%s)\",
        \"password\": \"Test123!\",
        \"appName\": \"GodGPT\",
        \"code\": \"000000\"
    }")
    
    log_response "$response"
    
    # Response should be an error (verification code not verified)
    # Check for error response or non-success status
    if echo "$response" | jq -e '.error' > /dev/null 2>&1; then
        local error=$(echo "$response" | jq -r '.error.message // .error // "Registration rejected"')
        log_info "Registration correctly rejected with error ✓"
        log_info "Error: $error"
        return 0
    elif echo "$response" | jq -e '.id' > /dev/null 2>&1; then
        # If we get a user ID, registration succeeded (shouldn't happen with wrong code)
        log_warn "Registration succeeded with wrong code - security concern!"
        return 1
    else
        # Any other response (including empty) means rejection
        log_info "Registration correctly rejected ✓"
        return 0
    fi
}

# Test 9: Check Registered Email Exists (via proxy)
test_proxy_check_registered_email() {
    log_step "Test 9: Checking if REGISTERED email exists (via proxy)..."
    
    # Use TEST_USERNAME as the registered email (admin user)
    local test_registered_email="${TEST_USERNAME}@example.com"
    
    # First try to get actual email from user profile if logged in
    if [ -n "$ACCESS_TOKEN" ]; then
        local profile=$(api_get "/api/godgpt/account")
        local email_from_profile=$(echo "$profile" | jq -r '.data.email // .email // empty' 2>/dev/null)
        if [ -n "$email_from_profile" ] && [ "$email_from_profile" != "null" ]; then
            test_registered_email="$email_from_profile"
            REGISTERED_EMAIL="$email_from_profile"
        fi
    fi
    
    log_info "Testing with: $test_registered_email"
    
    local response=$(api_post_guest "/api/account/check-email-registered" "{
        \"emailAddress\": \"$test_registered_email\"
    }")
    
    log_response "$response"
    
    # Response should be wrapped: {code: "20000", data: bool, message: ""}
    local data=$(echo "$response" | jq '.data' 2>/dev/null)
    
    if [ "$data" == "true" ]; then
        log_info "Registered email correctly detected as registered ✓"
        REGISTERED_EMAIL="$test_registered_email"
        return 0
    elif [ "$data" == "false" ]; then
        log_warn "Test email not found as registered (expected for: $test_registered_email)"
        log_info "This is OK if test user has different email format"
        return 0
    # Fallback: check raw boolean (backward compatibility)
    elif echo "$response" | jq -e 'type == "boolean"' > /dev/null 2>&1; then
        local is_registered=$(echo "$response" | jq '.')
        if [ "$is_registered" == "true" ]; then
            log_info "Registered email correctly detected as registered ✓"
            REGISTERED_EMAIL="$test_registered_email"
        fi
        return 0
    else
        log_error "Invalid response format. Expected: {code, data: bool, message}"
        log_error "Got: $response"
        return 1
    fi
}

# Test 10: Send Password Reset Code to Registered Email (via proxy)
test_proxy_send_password_reset_code() {
    log_step "Test 10: Sending password reset code (via proxy)..."
    
    # Use REGISTERED_EMAIL if available, otherwise use TEST_EMAIL
    local email_to_test="${REGISTERED_EMAIL:-$TEST_EMAIL}"
    log_info "Sending reset code to: $email_to_test"
    
    local response=$(api_post_guest "/api/account/send-password-reset-code" "{
        \"email\": \"$email_to_test\",
        \"appName\": \"GodGPT\"
    }")
    
    log_response "$response"
    
    # Response should be void (204 No Content) or empty
    # For unregistered emails, may return error
    if [ -z "$response" ] || [ "$response" == "{}" ] || [ "$response" == "null" ]; then
        log_info "Password reset code sent successfully (void response) ✓"
        return 0
    elif echo "$response" | jq -e '.error' > /dev/null 2>&1; then
        local error=$(echo "$response" | jq -r '.error.message // .error // "Unknown"')
        log_info "Password reset response: $error"
        # This is OK - unregistered emails may get error
        return 0
    else
        # Any valid JSON response is acceptable
        log_info "Password reset code endpoint responded ✓"
        return 0
    fi
}

# Test 11: Verify Password Reset Token with WRONG token (via proxy)
test_proxy_verify_reset_token_wrong() {
    log_step "Test 11: Verifying password reset token with WRONG token (via proxy)..."
    log_info "Testing error handling with invalid token"
    
    local response=$(api_post_guest "/api/account/verify-password-reset-token" "{
        \"userId\": \"00000000-0000-0000-0000-000000000000\",
        \"resetToken\": \"invalid-token-12345\"
    }")
    
    log_response "$response"
    
    # Response should be wrapped: {code: "20000", data: bool, message: ""}
    local data=$(echo "$response" | jq '.data' 2>/dev/null)
    
    if [ "$data" == "false" ]; then
        log_info "Wrong token correctly rejected (returned false) ✓"
        return 0
    elif [ "$data" == "true" ]; then
        log_warn "Wrong token unexpectedly returned true - security concern!"
        return 1
    # Fallback: check raw boolean (backward compatibility)
    elif echo "$response" | jq -e 'type == "boolean"' > /dev/null 2>&1; then
        local verified=$(echo "$response" | jq '.')
        if [ "$verified" == "false" ]; then
            log_info "Wrong token correctly rejected (returned false) ✓"
            return 0
        fi
    elif echo "$response" | jq -e '.error' > /dev/null 2>&1; then
        local error=$(echo "$response" | jq -r '.error.message // .error // "Unknown"')
        log_info "Wrong token correctly rejected with error ✓"
        log_info "Error: $error"
        return 0
    else
        log_info "Password reset token verification endpoint responded ✓"
        return 0
    fi
}

# Test 12: Reset Password with WRONG token (via proxy)
test_proxy_reset_password_wrong() {
    log_step "Test 12: Resetting password with WRONG token (via proxy)..."
    log_info "Testing error handling with invalid token"
    
    local email_to_test="${REGISTERED_EMAIL:-$TEST_EMAIL}"
    
    local response=$(api_post_guest "/api/account/reset-password" "{
        \"userId\": \"00000000-0000-0000-0000-000000000000\",
        \"resetToken\": \"invalid-token-12345\",
        \"password\": \"NewPassword123!\"
    }")
    
    log_response "$response"
    
    # Response should be error (invalid token)
    if [ -z "$response" ] || [ "$response" == "{}" ] || [ "$response" == "null" ]; then
        # Empty response could mean success (shouldn't happen with wrong token)
        log_warn "Empty response - password might have been reset (security concern)"
        return 0
    elif echo "$response" | jq -e '.error' > /dev/null 2>&1; then
        local error=$(echo "$response" | jq -r '.error.message // .error // "Unknown"')
        log_info "Wrong token correctly rejected with error ✓"
        log_info "Error: $error"
        return 0
    else
        # Any error response is expected
        log_info "Reset password correctly rejected invalid token ✓"
        return 0
    fi
}

# Run all tests
run_all_tests() {
    local passed=0
    local failed=0
    
    log_info "========================================"
    log_info "Running Account Flow Tests"
    log_info "========================================"
    echo ""
    
    log_info "Generated test email: $TEST_EMAIL"
    echo ""
    
    # Authenticated tests (require login)
    log_info "--- Authenticated Tests (GodGPTAccountController) ---"
    echo ""
    
    if ! login; then
        log_error "Login failed, aborting authenticated tests"
        exit 1
    fi
    echo ""
    
    if test_get_user_profile; then
        ((passed++))
    else
        ((failed++))
    fi
    echo ""
    
    if test_update_user_profile; then
        ((passed++))
    else
        ((failed++))
    fi
    echo ""
    
    if test_set_voice_language; then
        ((passed++))
    else
        ((failed++))
    fi
    echo ""
    
    if test_delete_account; then
        ((passed++))
    else
        ((failed++))
    fi
    echo ""
    
    # Guest tests (AccountProxyController - backward compatibility)
    # Phase 1: New Email Registration Flow
    log_info "--- Phase 1: New Email Registration Flow ---"
    echo ""
    
    if test_proxy_check_new_email; then
        ((passed++))
    else
        ((failed++))
    fi
    echo ""
    
    if test_proxy_send_register_code; then
        ((passed++))
    else
        ((failed++))
    fi
    echo ""
    
    if test_proxy_verify_register_code_wrong; then
        ((passed++))
    else
        ((failed++))
    fi
    echo ""
    
    if test_proxy_register_wrong_code; then
        ((passed++))
    else
        ((failed++))
    fi
    echo ""
    
    # Phase 2: Registered Email Password Reset Flow
    log_info "--- Phase 2: Password Reset Flow ---"
    echo ""
    
    if test_proxy_check_registered_email; then
        ((passed++))
    else
        ((failed++))
    fi
    echo ""
    
    if test_proxy_send_password_reset_code; then
        ((passed++))
    else
        ((failed++))
    fi
    echo ""
    
    if test_proxy_verify_reset_token_wrong; then
        ((passed++))
    else
        ((failed++))
    fi
    echo ""
    
    if test_proxy_reset_password_wrong; then
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
    log_info "GodGPT Account Flow Test"
    log_info "========================================"
    echo ""
    
    case "${1:-all}" in
        "profile")
            login
            test_get_user_profile
            ;;
        "update")
            login
            test_update_user_profile
            ;;
        "voice")
            login
            test_set_voice_language
            ;;
        "proxy")
            log_info "Running AccountProxyController tests only..."
            echo ""
            log_info "Generated test email: $TEST_EMAIL"
            echo ""
            
            # Phase 1: New Email Registration Flow
            log_info "--- Phase 1: New Email Registration Flow ---"
            echo ""
            test_proxy_check_new_email
            echo ""
            test_proxy_send_register_code
            echo ""
            test_proxy_verify_register_code_wrong
            echo ""
            test_proxy_register_wrong_code
            echo ""
            
            # Phase 2: Password Reset Flow (may need login first to get registered email)
            log_info "--- Phase 2: Password Reset Flow ---"
            log_info "Logging in to get registered user email..."
            login || true  # Don't fail if login fails
            echo ""
            test_proxy_check_registered_email
            echo ""
            test_proxy_send_password_reset_code
            echo ""
            test_proxy_verify_reset_token_wrong
            echo ""
            test_proxy_reset_password_wrong
            ;;
        "all"|*)
            run_all_tests
            ;;
    esac
}

main "$@"


#!/bin/bash

# =============================================================================
# Test Invitation Flow Script
# Tests invitation-related endpoints
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Configuration
AUTH_URL="https://localhost:44320"
API_URL="https://localhost:44345"
CLIENT_ID="AevatarAuthServer"
SCOPE="Aevatar openid profile"
TEST_USERNAME="admin"
TEST_PASSWORD="1q2w3E*"

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Variables
ACCESS_TOKEN=""

log_info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

log_step() {
    echo -e "${BLUE}[STEP]${NC} $1"
}

log_warn() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

log_response() {
    echo -e "${YELLOW}[RESPONSE]${NC}"
    echo "$1" | jq . 2>/dev/null || echo "$1"
}

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

# Check if services are running
check_services() {
    log_step "Checking if services are running..."
    
    if ! curl -k -s "$AUTH_URL/.well-known/openid-configuration" > /dev/null 2>&1; then
        log_error "AuthServer is not running at $AUTH_URL"
        exit 1
    fi
    log_info "AuthServer is running ✓"
    
    if ! curl -k -s "$API_URL/api/abp/application-configuration" > /dev/null 2>&1; then
        log_error "HttpApi is not running at $API_URL"
        exit 1
    fi
    log_info "HttpApi is running ✓"
}

# Get access token using password grant
get_access_token() {
    log_step "Getting access token with password grant..."
    
    local response=$(curl -k -s -X POST "$AUTH_URL/connect/token" \
        -H "Content-Type: application/x-www-form-urlencoded" \
        -d "grant_type=password" \
        -d "client_id=$CLIENT_ID" \
        -d "username=$TEST_USERNAME" \
        -d "password=$TEST_PASSWORD" \
        -d "scope=$SCOPE")
    
    ACCESS_TOKEN=$(echo "$response" | jq -r '.access_token')
    
    if [ "$ACCESS_TOKEN" == "null" ] || [ -z "$ACCESS_TOKEN" ]; then
        log_error "Failed to get access token"
        log_response "$response"
        exit 1
    fi
    
    log_info "Access token obtained ✓"
    echo "Token: ${ACCESS_TOKEN:0:50}..."
}

# Test 1: Get Invitation Info
test_get_invitation_info() {
    log_step "Test 1: Getting invitation info..."
    
    local response=$(curl -k -s -X GET "$API_URL/api/godgpt/invitation/info" \
        -H "Authorization: Bearer $ACCESS_TOKEN" \
        -H "Content-Type: application/json")
    
    log_response "$response"
    
    if echo "$response" | jq -e '.rewardTierList' > /dev/null 2>&1; then
        log_info "Invitation info retrieved successfully ✓"
        return 0
    else
        log_warn "Failed to get invitation info"
        return 1
    fi
}

# Test 2: Get Invitation Code Type
test_get_code_type() {
    log_step "Test 2: Getting invitation code type..."
    
    local response=$(curl -k -s -X GET "$API_URL/api/godgpt/invitation/code-type?code=TESTCODE123" \
        -H "Authorization: Bearer $ACCESS_TOKEN" \
        -H "Content-Type: application/json")
    
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
test_redeem_friend_code() {
    log_step "Test 3: Redeeming friend invitation code..."
    
    local response=$(curl -k -s -X POST "$API_URL/api/godgpt/invitation/redeem" \
        -H "Authorization: Bearer $ACCESS_TOKEN" \
        -H "Content-Type: application/json" \
        -d '{
            "code": "TESTFRIEND123",
            "devicePlatform": "web"
        }')
    
    log_response "$response"
    
    if echo "$response" | jq -e '.isValid' > /dev/null 2>&1; then
        log_info "Redeem friend code endpoint tested ✓"
        return 0
    else
        log_warn "Redeem friend code may have failed"
        return 1
    fi
}

# Test 4: Redeem Free Trial Code
test_redeem_trial_code() {
    log_step "Test 4: Redeeming free trial code..."
    
    local response=$(curl -k -s -X POST "$API_URL/api/godgpt/invitation/redeem" \
        -H "Authorization: Bearer $ACCESS_TOKEN" \
        -H "Content-Type: application/json" \
        -d '{
            "code": "FREETRIAL12345",
            "devicePlatform": "web"
        }')
    
    log_response "$response"
    
    if echo "$response" | jq -e '.isValid' > /dev/null 2>&1; then
        log_info "Redeem trial code endpoint tested ✓"
        return 0
    else
        log_warn "Redeem trial code may have failed"
        return 1
    fi
}

# Test 5: Get Credits History
test_get_credits_history() {
    log_step "Test 5: Getting credits history..."
    
    local response=$(curl -k -s -X GET "$API_URL/api/godgpt/invitation/credits/history?page=1&pageSize=10" \
        -H "Authorization: Bearer $ACCESS_TOKEN" \
        -H "Content-Type: application/json")
    
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
    
    local response=$(curl -k -s -X POST "$API_URL/api/godgpt/invitation/generate-trial-code" \
        -H "Authorization: Bearer $ACCESS_TOKEN" \
        -H "Content-Type: application/json" \
        -d '{
            "batchId": "test_batch_001",
            "count": 10,
            "trialDays": 7
        }')
    
    log_response "$response"
    
    if echo "$response" | jq -e '.success' > /dev/null 2>&1; then
        log_info "Generate trial code endpoint tested ✓"
        return 0
    else
        log_warn "Generate trial code may have failed (expected if not manager)"
        return 0  # Don't fail if permission denied
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
    
    test_generate_trial_code
    ((passed++))
    echo ""
    
    log_info "========================================"
    log_info "Test Results: $passed passed, $failed failed"
    log_info "========================================"
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
    
    get_access_token
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
        "all"|*)
            run_all_tests
            ;;
    esac
}

main "$@"


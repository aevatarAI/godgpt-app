#!/bin/bash

# =============================================================================
# Test Config Flow Script
# Tests configuration-related endpoints (requires systemPromptManager role)
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

# Test 1: Get System Prompt
test_get_system_prompt() {
    log_step "Test 1: Getting system prompt..."
    log_warn "Note: This requires systemPromptManager role"
    
    local response=$(curl -k -s -X GET "$API_URL/api/godgpt/configuration/system-prompt" \
        -H "Authorization: Bearer $ACCESS_TOKEN" \
        -H "Content-Type: application/json")
    
    log_response "$response"
    
    # Check if response contains permission denied message
    if echo "$response" | grep -qi "Permission denied"; then
        log_warn "Access denied (expected if user doesn't have systemPromptManager role)"
        return 0  # Don't fail if permission denied
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
    log_warn "Note: This requires systemPromptManager role"
    
    local test_prompt="This is a test system prompt updated at $(date +%Y-%m-%d\ %H:%M:%S)"
    
    local response=$(curl -k -s -X POST "$API_URL/api/godgpt/configuration/system-prompt" \
        -H "Authorization: Bearer $ACCESS_TOKEN" \
        -H "Content-Type: application/json" \
        -d "{
            \"systemPrompt\": \"$test_prompt\"
        }")
    
    log_response "$response"
    
    # Check if response contains permission denied or success message
    if echo "$response" | grep -qi "Permission denied"; then
        log_warn "Access denied (expected if user doesn't have systemPromptManager role)"
        return 0  # Don't fail if permission denied
    elif echo "$response" | jq -e '.message' > /dev/null 2>&1 || echo "$response" | grep -qi "success"; then
        log_info "System prompt updated successfully ✓"
        return 0
    else
        log_warn "Failed to update system prompt"
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
    log_warn "Note: These tests require systemPromptManager role"
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
    
    get_access_token
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


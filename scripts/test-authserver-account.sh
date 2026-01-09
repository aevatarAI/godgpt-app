#!/bin/bash

# ============================================
# AuthServer Account API Test Script
# ============================================
# Usage:
#   ./test-authserver-account.sh              # Full test with real email
#   ./test-authserver-account.sh --skip-email # Skip real email sending
#   ./test-authserver-account.sh --check-cache # Also verify Redis cache
#   ./test-authserver-account.sh --register   # Full registration flow (interactive)
#   ./test-authserver-account.sh --register --code=123456 # Auto register with code
# ============================================

AUTH_SERVER_URL="${AUTH_SERVER_URL:-http://localhost:8001}"
TEST_EMAIL="${TEST_EMAIL:-test-$(date +%s)@example.com}"
TEST_USERNAME="${TEST_USERNAME:-testuser_$(date +%s)}"
TEST_PASSWORD="${TEST_PASSWORD:-Test1234!}"
REDIS_CLI="${REDIS_CLI:-redis-cli}"
SKIP_EMAIL=false
CHECK_CACHE=false
DO_REGISTER=false
VERIFICATION_CODE=""

# Parse arguments
for arg in "$@"; do
    case $arg in
        --skip-email)
            SKIP_EMAIL=true
            shift
            ;;
        --check-cache)
            CHECK_CACHE=true
            shift
            ;;
        --email=*)
            TEST_EMAIL="${arg#*=}"
            shift
            ;;
        --username=*)
            TEST_USERNAME="${arg#*=}"
            shift
            ;;
        --password=*)
            TEST_PASSWORD="${arg#*=}"
            shift
            ;;
        --register)
            DO_REGISTER=true
            shift
            ;;
        --code=*)
            VERIFICATION_CODE="${arg#*=}"
            shift
            ;;
        --auto-code)
            AUTO_GET_CODE=true
            shift
            ;;
    esac
done

# Function to get verification code from Redis
# ABP uses Hash type with format: c:System.String,k:{KeyPrefix}{CacheKey}
# Data is stored in "data" field as quoted string
get_code_from_redis() {
    local email="$1"
    local app_name="${2:-godgpt}"
    # ABP Redis key format: c:System.String,k:GodGPT:RegisterCode_{appName}_{email}
    local key="c:System.String,k:GodGPT:RegisterCode_${app_name}_$(echo $email | tr '[:upper:]' '[:lower:]')"
    
    echo -e "  ${CYAN}Checking Redis key: ${key}${NC}" >&2
    
    if command -v redis-cli &> /dev/null; then
        # ABP stores data as Hash with "data" field containing quoted string
        local code=$(redis-cli HGET "$key" "data" 2>/dev/null | tr -d '"')
        if [ -n "$code" ] && [ "$code" != "(nil)" ]; then
            echo -e "  ${GREEN}Found code in Redis: ${code}${NC}" >&2
            echo "$code"
            return 0
        else
            echo -e "  ${YELLOW}Code not found in Redis${NC}" >&2
        fi
    else
        echo -e "  ${YELLOW}redis-cli not available${NC}" >&2
    fi
    return 1
}

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

echo -e "${BLUE}============================================${NC}"
echo -e "${BLUE}  AuthServer Account API Tests${NC}"
echo -e "${BLUE}  URL: ${AUTH_SERVER_URL}${NC}"
echo -e "${BLUE}  Email: ${TEST_EMAIL}${NC}"
echo -e "${BLUE}  Skip Email: ${SKIP_EMAIL}${NC}"
echo -e "${BLUE}  Check Cache: ${CHECK_CACHE}${NC}"
echo -e "${BLUE}  Do Register: ${DO_REGISTER}${NC}"
echo -e "${BLUE}============================================${NC}"
echo ""

# Function to test an endpoint
test_endpoint() {
    local method="$1"
    local endpoint="$2"
    local data="$3"
    local expected_pattern="$4"
    local description="$5"
    local allow_error="${6:-false}"  # Optional: allow error responses
    
    echo -e "${YELLOW}Testing: ${description}${NC}"
    echo -e "  ${method} ${endpoint}"
    
    if [ -n "$data" ]; then
        response=$(curl -s -X "$method" "${AUTH_SERVER_URL}${endpoint}" \
            -H "Content-Type: application/json" \
            -d "$data" 2>&1)
    else
        response=$(curl -s -X "$method" "${AUTH_SERVER_URL}${endpoint}" 2>&1)
    fi
    
    echo -e "  Response: ${response}"
    
    # First check for error response (unless explicitly allowed)
    if [ "$allow_error" != "true" ] && echo "$response" | grep -q '"error"'; then
        error_msg=$(echo "$response" | grep -o '"message":"[^"]*"' | head -1 | cut -d'"' -f4)
        echo -e "  ${RED}✗ FAIL (API returned error: ${error_msg})${NC}"
        return 1
    fi
    
    if echo "$response" | grep -q "$expected_pattern"; then
        echo -e "  ${GREEN}✓ PASS${NC}"
        return 0
    else
        echo -e "  ${RED}✗ FAIL (expected pattern: ${expected_pattern})${NC}"
        return 1
    fi
}

# Function to check Redis cache
check_redis_key() {
    local key_pattern="$1"
    local description="$2"
    
    echo -e "${CYAN}Checking Redis: ${description}${NC}"
    echo -e "  Pattern: ${key_pattern}"
    
    keys=$($REDIS_CLI KEYS "*${key_pattern}*" 2>/dev/null)
    
    if [ -z "$keys" ]; then
        echo -e "  ${RED}✗ No keys found${NC}"
        return 1
    else
        echo -e "  ${GREEN}✓ Found keys: ${keys}${NC}"
        for key in $keys; do
            value=$($REDIS_CLI GET "$key" 2>/dev/null)
            echo -e "  Value: ${value}"
        done
        return 0
    fi
}

# Track test results
PASSED=0
FAILED=0

echo -e "${BLUE}--- Basic Endpoint Tests ---${NC}"
echo ""

# Test 1: Logout (anonymous)
if test_endpoint "POST" "/api/app/account/logout" "" "success.*true" "Logout (anonymous)"; then
    ((PASSED++))
else
    ((FAILED++))
fi
echo ""

# Test 2: Check email registered (not registered)
if test_endpoint "POST" "/api/app/account/check-email-registered" \
    '{"emailAddress":"nonexistent@example.com"}' \
    "false" \
    "Check email registered (should be false)"; then
    ((PASSED++))
else
    ((FAILED++))
fi
echo ""

# Test 3: Verify register code (no code exists)
if test_endpoint "POST" "/api/app/account/verify-register-code" \
    '{"email":"test@example.com","code":"123456","appName":"GodGPT"}' \
    "false" \
    "Verify register code (should be false - no code)"; then
    ((PASSED++))
else
    ((FAILED++))
fi
echo ""

# Test 3b: Verify register code with lowercase appName (important!)
if test_endpoint "POST" "/api/app/account/verify-register-code" \
    '{"email":"test@example.com","code":"123456","appName":"godgpt"}' \
    "false" \
    "Verify register code with lowercase appName (should be false - no code)"; then
    ((PASSED++))
else
    ((FAILED++))
fi
echo ""

# Test 4: Verify password reset token (invalid)
if test_endpoint "POST" "/api/app/account/verify-password-reset-token" \
    '{"userId":"00000000-0000-0000-0000-000000000000","resetToken":"invalid"}' \
    "false" \
    "Verify password reset token (should be false)"; then
    ((PASSED++))
else
    ((FAILED++))
fi
echo ""

echo -e "${BLUE}--- Send Register Code Tests ---${NC}"
echo ""

if [ "$SKIP_EMAIL" = true ]; then
    echo -e "${YELLOW}⏭ SKIPPED: Real email sending (--skip-email flag)${NC}"
    echo ""
else
    echo -e "${YELLOW}Testing: Send register code (GodGPT) - Real Email${NC}"
    echo -e "  POST /api/app/account/send-register-code"
    echo -e "  Email: ${TEST_EMAIL}"
    response=$(curl -s -X POST "${AUTH_SERVER_URL}/api/app/account/send-register-code" \
        -H "Content-Type: application/json" \
        -d "{\"email\":\"${TEST_EMAIL}\",\"appName\":\"GodGPT\",\"platform\":0}" 2>&1)
    echo -e "  Response: ${response}"
    
    if echo "$response" | grep -q '"success":true'; then
        echo -e "  ${GREEN}✓ PASS (email sent successfully)${NC}"
        ((PASSED++))
    elif echo "$response" | grep -q 'error'; then
        echo -e "  ${RED}✗ FAIL (email sending failed)${NC}"
        ((FAILED++))
    else
        echo -e "  ${YELLOW}? UNKNOWN response${NC}"
        ((FAILED++))
    fi
    echo ""

    if [ "$CHECK_CACHE" = true ]; then
        echo -e "${BLUE}--- Cache Verification ---${NC}"
        echo ""
        
        if command -v $REDIS_CLI &> /dev/null; then
            email_lower=$(echo "$TEST_EMAIL" | tr '[:upper:]' '[:lower:]')
            if check_redis_key "RegisterCode_GodGPT_${email_lower}" "Register code for ${TEST_EMAIL}"; then
                ((PASSED++))
            else
                echo -e "  ${YELLOW}Trying alternative key patterns...${NC}"
                all_keys=$($REDIS_CLI KEYS "*RegisterCode*" 2>/dev/null)
                if [ -n "$all_keys" ]; then
                    echo -e "  ${GREEN}Found RegisterCode keys: ${all_keys}${NC}"
                    ((PASSED++))
                else
                    echo -e "  ${RED}✗ No RegisterCode keys found in Redis${NC}"
                    ((FAILED++))
                fi
            fi
        else
            echo -e "  ${YELLOW}? SKIP (redis-cli not found)${NC}"
        fi
        echo ""
    fi
fi

echo -e "${BLUE}--- Language Header Tests ---${NC}"
echo ""

echo -e "${YELLOW}Testing: Logout with Chinese language header${NC}"
response=$(curl -s -X POST "${AUTH_SERVER_URL}/api/app/account/logout" \
    -H "Content-Type: application/json" \
    -H "GodGPTLanguage: zh-cn" 2>&1)
echo -e "  Response: ${response}"
if echo "$response" | grep -q "success.*true"; then
    echo -e "  ${GREEN}✓ PASS${NC}"
    ((PASSED++))
else
    echo -e "  ${RED}✗ FAIL${NC}"
    ((FAILED++))
fi
echo ""

echo -e "${YELLOW}Testing: Logout with Spanish language header${NC}"
response=$(curl -s -X POST "${AUTH_SERVER_URL}/api/app/account/logout" \
    -H "Content-Type: application/json" \
    -H "GodGPTLanguage: es" 2>&1)
echo -e "  Response: ${response}"
if echo "$response" | grep -q "success.*true"; then
    echo -e "  ${GREEN}✓ PASS${NC}"
    ((PASSED++))
else
    echo -e "  ${RED}✗ FAIL${NC}"
    ((FAILED++))
fi
echo ""

echo -e "${BLUE}--- Multi-App Support Tests ---${NC}"
echo ""

if test_endpoint "POST" "/api/app/account/check-email-registered" \
    '{"emailAddress":"lumen-test@example.com","appName":"Lumen"}' \
    "false" \
    "Check email registered (Lumen app)"; then
    ((PASSED++))
else
    ((FAILED++))
fi
echo ""

echo -e "${BLUE}--- AppName Case Sensitivity Test ---${NC}"
echo -e "${CYAN}This test verifies that appName is case-insensitive${NC}"
echo ""

# Test: Send code with 'GodGPT', verify with 'godgpt' (lowercase)
# This tests the fix for the appName case sensitivity bug
if [ "$SKIP_EMAIL" = false ]; then
    CASE_TEST_EMAIL="case-test-$(date +%s)@example.com"
    echo -e "${YELLOW}Sending verification code with appName='GodGPT'${NC}"
    send_resp=$(curl -s -X POST "${AUTH_SERVER_URL}/api/app/account/send-register-code" \
        -H "Content-Type: application/json" \
        -d "{\"email\":\"${CASE_TEST_EMAIL}\",\"appName\":\"GodGPT\",\"platform\":0}" 2>&1)
    echo -e "  Response: ${send_resp}"
    
    if echo "$send_resp" | grep -q '"success":true'; then
        echo -e "  ${GREEN}✓ Code sent successfully${NC}"
        ((PASSED++))
        
        echo ""
        echo -e "${YELLOW}Verifying code with appName='godgpt' (lowercase)${NC}"
        echo -e "${CYAN}Enter the code from email (or press Enter to skip):${NC}"
        read -t 30 -p "  Code: " CASE_TEST_CODE
        
        if [ -n "$CASE_TEST_CODE" ]; then
            verify_resp=$(curl -s -X POST "${AUTH_SERVER_URL}/api/app/account/verify-register-code" \
                -H "Content-Type: application/json" \
                -d "{\"email\":\"${CASE_TEST_EMAIL}\",\"code\":\"${CASE_TEST_CODE}\",\"appName\":\"godgpt\"}" 2>&1)
            echo -e "  Response: ${verify_resp}"
            
            if echo "$verify_resp" | grep -q "true"; then
                echo -e "  ${GREEN}✓ PASS - AppName is case-insensitive (FIXED!)${NC}"
                ((PASSED++))
            elif echo "$verify_resp" | grep -q "false"; then
                echo -e "  ${RED}✗ FAIL - AppName case sensitivity BUG! Code not found when using lowercase appName${NC}"
                ((FAILED++))
            fi
        else
            echo -e "  ${YELLOW}⏭ Skipped (no code entered)${NC}"
        fi
    else
        echo -e "  ${RED}✗ Failed to send code${NC}"
        ((FAILED++))
    fi
    echo ""
fi

# ===========================================
# Full Registration Flow Test
# ===========================================
if [ "$DO_REGISTER" = true ]; then
    echo -e "${BLUE}--- Full Registration Flow Test ---${NC}"
    echo ""
    
    # Use a unique email for registration flow to avoid rate limiting from basic tests
    REGISTER_EMAIL="register-$(date +%s)@example.com"
    echo -e "${CYAN}Using unique email for registration: ${REGISTER_EMAIL}${NC}"
    echo ""
    
    # Step 1: Check if email is already registered
    echo -e "${YELLOW}Step 1: Check if email is already registered${NC}"
    check_response=$(curl -s -X POST "${AUTH_SERVER_URL}/api/app/account/check-email-registered" \
        -H "Content-Type: application/json" \
        -d "{\"emailAddress\":\"${REGISTER_EMAIL}\",\"appName\":\"GodGPT\"}" 2>&1)
    echo -e "  Response: ${check_response}"
    
    if echo "$check_response" | grep -q "true"; then
        echo -e "  ${YELLOW}⚠ Email already registered, skipping registration test${NC}"
    else
        echo -e "  ${GREEN}✓ Email not registered, proceeding...${NC}"
        ((PASSED++))
        
        # Step 2: Send verification code
        echo ""
        echo -e "${YELLOW}Step 2: Send verification code to ${REGISTER_EMAIL}${NC}"
        send_response=$(curl -s -X POST "${AUTH_SERVER_URL}/api/app/account/send-register-code" \
            -H "Content-Type: application/json" \
            -d "{\"email\":\"${REGISTER_EMAIL}\",\"appName\":\"GodGPT\",\"platform\":0}" 2>&1)
        echo -e "  Response: ${send_response}"
        
        if echo "$send_response" | grep -q '"error"'; then
            error_msg=$(echo "$send_response" | grep -o '"message":"[^"]*"' | head -1 | cut -d'"' -f4)
            echo -e "  ${RED}✗ Failed to send verification code: ${error_msg}${NC}"
            ((FAILED++))
        elif echo "$send_response" | grep -q '"success":true'; then
            echo -e "  ${GREEN}✓ Verification code sent${NC}"
            ((PASSED++))
            
            # Step 3: Get verification code (from Redis, parameter, or interactive)
            echo ""
            echo -e "${CYAN}Step 3: Get verification code${NC}"
            
            if [ -z "$VERIFICATION_CODE" ]; then
                # Try to get code from Redis first
                echo -e "  Attempting to get code from Redis..."
                REDIS_CODE=$(get_code_from_redis "$REGISTER_EMAIL" "godgpt")
                
                if [ -n "$REDIS_CODE" ] && [ "$REDIS_CODE" != "" ]; then
                    VERIFICATION_CODE="$REDIS_CODE"
                    echo -e "  ${GREEN}✓ Got code from Redis: ${VERIFICATION_CODE}${NC}"
                else
                    echo -e "  ${YELLOW}Could not get code from Redis${NC}"
                    echo -e "  Please check your email (${REGISTER_EMAIL}) for the code."
                    read -p "  Enter 6-digit code (or press Enter to skip): " VERIFICATION_CODE
                fi
            else
                echo -e "  Using provided code: ${VERIFICATION_CODE}"
            fi
            
            if [ -n "$VERIFICATION_CODE" ]; then
                # Step 4: Verify the code first (optional validation)
                echo ""
                echo -e "${YELLOW}Step 4: Verify code before registration${NC}"
                verify_response=$(curl -s -X POST "${AUTH_SERVER_URL}/api/app/account/verify-register-code" \
                    -H "Content-Type: application/json" \
                    -d "{\"email\":\"${REGISTER_EMAIL}\",\"code\":\"${VERIFICATION_CODE}\",\"appName\":\"GodGPT\"}" 2>&1)
                echo -e "  Response: ${verify_response}"
                
                if echo "$verify_response" | grep -q '"error"'; then
                    error_msg=$(echo "$verify_response" | grep -o '"message":"[^"]*"' | head -1 | cut -d'"' -f4)
                    echo -e "  ${RED}✗ Verification error: ${error_msg}${NC}"
                    ((FAILED++))
                elif echo "$verify_response" | grep -q "true"; then
                    echo -e "  ${GREEN}✓ Code verified${NC}"
                    ((PASSED++))
                else
                    echo -e "  ${YELLOW}⚠ Code verification returned false${NC}"
                    ((FAILED++))
                fi
                
                # Step 5: Complete registration
                echo ""
                echo -e "${YELLOW}Step 5: Complete registration${NC}"
                REGISTER_USERNAME="testuser_$(date +%s)"
                echo -e "  Email: ${REGISTER_EMAIL}"
                echo -e "  Username: ${REGISTER_USERNAME}"
                register_data=$(cat <<EOF
{
    "emailAddress": "${REGISTER_EMAIL}",
    "userName": "${REGISTER_USERNAME}",
    "password": "${TEST_PASSWORD}",
    "code": "${VERIFICATION_CODE}",
    "appName": "GodGPT"
}
EOF
)
                echo -e "  Request: ${register_data}"
                
                register_response=$(curl -s -X POST "${AUTH_SERVER_URL}/api/app/account/register" \
                    -H "Content-Type: application/json" \
                    -H "GodGPTLanguage: en" \
                    -d "${register_data}" 2>&1)
                echo -e "  Response: ${register_response}"
                
                if echo "$register_response" | grep -q '"id"'; then
                    echo -e "  ${GREEN}✓ REGISTRATION SUCCESSFUL!${NC}"
                    ((PASSED++))
                    
                    # Extract user ID
                    user_id=$(echo "$register_response" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
                    echo -e "  ${GREEN}New User ID: ${user_id}${NC}"
                elif echo "$register_response" | grep -q '"error"'; then
                    error_msg=$(echo "$register_response" | grep -o '"message":"[^"]*"' | head -1 | cut -d'"' -f4)
                    echo -e "  ${RED}✗ REGISTRATION FAILED: ${error_msg}${NC}"
                    ((FAILED++))
                else
                    echo -e "  ${RED}✗ REGISTRATION FAILED (unexpected response)${NC}"
                    ((FAILED++))
                fi
            else
                echo -e "  ${YELLOW}⏭ SKIPPED: No verification code provided${NC}"
            fi
        fi
    fi
    echo ""
fi

echo -e "${BLUE}============================================${NC}"
echo -e "${BLUE}  Test Summary${NC}"
echo -e "${BLUE}============================================${NC}"
echo -e "  ${GREEN}Passed: ${PASSED}${NC}"
echo -e "  ${RED}Failed: ${FAILED}${NC}"
echo -e "  Total:  $((PASSED + FAILED))"
echo ""

if [ $FAILED -eq 0 ]; then
    echo -e "${GREEN}All tests passed!${NC}"
    exit 0
else
    echo -e "${RED}Some tests failed.${NC}"
    exit 1
fi

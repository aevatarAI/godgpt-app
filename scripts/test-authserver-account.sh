#!/bin/bash

# ============================================
# AuthServer Account API Test Script
# ============================================
# Usage:
#   ./test-authserver-account.sh              # Full test with real email
#   ./test-authserver-account.sh --skip-email # Skip real email sending
#   ./test-authserver-account.sh --check-cache # Also verify Redis cache
# ============================================

AUTH_SERVER_URL="${AUTH_SERVER_URL:-http://localhost:8001}"
TEST_EMAIL="${TEST_EMAIL:-test-$(date +%s)@example.com}"
REDIS_CLI="${REDIS_CLI:-redis-cli}"
SKIP_EMAIL=false
CHECK_CACHE=false

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
    esac
done

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
echo -e "${BLUE}  Skip Email: ${SKIP_EMAIL}${NC}"
echo -e "${BLUE}  Check Cache: ${CHECK_CACHE}${NC}"
echo -e "${BLUE}============================================${NC}"
echo ""

# Function to test an endpoint
test_endpoint() {
    local method="$1"
    local endpoint="$2"
    local data="$3"
    local expected_pattern="$4"
    local description="$5"
    
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

    echo -e "${YELLOW}Validating: AuthServer did not use NullEmailSender${NC}"
    if [ -d "apps/Aevatar.App/src/Aevatar.AuthServer/Logs" ]; then
        if tail -80 apps/Aevatar.App/src/Aevatar.AuthServer/Logs/log-*.log 2>/dev/null | grep -q "USING NullEmailSender"; then
            echo -e "  ${RED}✗ FAIL (server used NullEmailSender)${NC}"
            ((FAILED++))
        else
            echo -e "  ${GREEN}✓ PASS (no NullEmailSender detected)${NC}"
            ((PASSED++))
        fi
    else
        echo -e "  ${YELLOW}? SKIP (no local logs dir found)${NC}"
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

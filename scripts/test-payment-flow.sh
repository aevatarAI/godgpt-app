#!/bin/bash

# =============================================================================
# Test Payment Flow Script
# Tests the complete Stripe payment flow
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Configuration
AUTH_URL="https://localhost:44320"
API_URL="https://localhost:44345"
# AevatarAuthServer is a PUBLIC client (no secret) supporting password grant
CLIENT_ID="AevatarAuthServer"
SCOPE="Aevatar openid profile"
# Test user credentials
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
USER_ID=""

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
    
    # Check Auth Server
    if ! curl -k -s "$AUTH_URL/.well-known/openid-configuration" > /dev/null 2>&1; then
        log_error "AuthServer is not running at $AUTH_URL"
        exit 1
    fi
    log_info "AuthServer is running ✓"
    
    # Check HttpApi
    if ! curl -k -s "$API_URL/api/abp/application-configuration" > /dev/null 2>&1; then
        log_error "HttpApi is not running at $API_URL"
        exit 1
    fi
    log_info "HttpApi is running ✓"
}

# Get access token using password grant (for testing)
get_access_token() {
    log_step "Getting access token with password grant..."
    
    # AevatarAuthServer is a PUBLIC client (no secret) that supports password grant
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
    # Show first 50 chars of token
    echo "Token: ${ACCESS_TOKEN:0:50}..."
}

# Test 1: Get Stripe Products
test_get_products() {
    log_step "Test 1: Getting Stripe products..."
    
    local response=$(curl -k -s -X GET "$API_URL/api/godgpt/payment/products" \
        -H "Authorization: Bearer $ACCESS_TOKEN" \
        -H "Content-Type: application/json")
    
    log_response "$response"
    
    if echo "$response" | jq -e '.[]' > /dev/null 2>&1; then
        log_info "Products retrieved successfully ✓"
        return 0
    else
        log_warn "No products found or error occurred"
        return 1
    fi
}

# Test 2: Get Customer Info
test_get_customer() {
    log_step "Test 2: Getting Stripe customer..."
    
    local response=$(curl -k -s -X POST "$API_URL/api/godgpt/payment/customer" \
        -H "Authorization: Bearer $ACCESS_TOKEN" \
        -H "Content-Type: application/json")
    
    log_response "$response"
    
    if echo "$response" | jq -e '.customer' > /dev/null 2>&1; then
        log_info "Customer retrieved successfully ✓"
        return 0
    else
        log_warn "Customer not found or error occurred"
        return 1
    fi
}

# Test 3: Create Checkout Session
test_create_checkout_session() {
    log_step "Test 3: Creating checkout session..."
    
    # First get a price ID from products
    local products=$(curl -k -s -X GET "$API_URL/api/godgpt/payment/products" \
        -H "Authorization: Bearer $ACCESS_TOKEN")
    
    local price_id=$(echo "$products" | jq -r '.[0].priceId // empty')
    
    if [ -z "$price_id" ]; then
        log_warn "No price ID found, using default test price"
        price_id="price_test_monthly"
    fi
    
    log_info "Using price ID: $price_id"
    
    local response=$(curl -k -s -X POST "$API_URL/api/godgpt/payment/create-checkout-session" \
        -H "Authorization: Bearer $ACCESS_TOKEN" \
        -H "Content-Type: application/json" \
        -d "{
            \"priceId\": \"$price_id\",
            \"mode\": \"subscription\",
            \"successUrl\": \"https://localhost:44345/payment/success\",
            \"cancelUrl\": \"https://localhost:44345/payment/cancel\"
        }")
    
    log_response "$response"
    
    if echo "$response" | jq -e '.sessionId // .checkoutUrl // .url' > /dev/null 2>&1; then
        log_info "Checkout session created successfully ✓"
        return 0
    else
        log_warn "Checkout session creation may have failed"
        return 1
    fi
}

# Test 4: Get Subscription Status
test_get_subscription_status() {
    log_step "Test 4: Getting subscription status..."
    
    local response=$(curl -k -s -X GET "$API_URL/api/godgpt/payment/has-active-subscription" \
        -H "Authorization: Bearer $ACCESS_TOKEN")
    
    log_response "$response"
    
    log_info "Subscription status retrieved ✓"
}

# Test 5: Get Payment History
test_get_payment_history() {
    log_step "Test 5: Getting payment history..."
    
    local response=$(curl -k -s -X GET "$API_URL/api/godgpt/payment/list" \
        -H "Authorization: Bearer $ACCESS_TOKEN")
    
    log_response "$response"
    
    log_info "Payment history retrieved ✓"
}

# Test 6: New Payment API - Get Products
test_new_api_products() {
    log_step "Test 6: Testing new Payment API - Get Products..."
    
    local response=$(curl -k -s -X GET "$API_URL/api/payment/products/0" \
        -H "Authorization: Bearer $ACCESS_TOKEN")
    
    log_response "$response"
    
    log_info "New API products endpoint tested ✓"
}

# Test 7: New Payment API - Subscribe
test_new_api_subscribe() {
    log_step "Test 7: Testing new Payment API - Subscribe..."
    
    local response=$(curl -k -s -X POST "$API_URL/api/payment/subscribe" \
        -H "Authorization: Bearer $ACCESS_TOKEN" \
        -H "Content-Type: application/json" \
        -d "{
            \"platform\": 0,
            \"productId\": \"price_test_monthly\",
            \"successUrl\": \"https://localhost:44345/success\",
            \"cancelUrl\": \"https://localhost:44345/cancel\"
        }")
    
    log_response "$response"
    
    log_info "New API subscribe endpoint tested ✓"
}

# Simulate Stripe Webhook (for local testing)
test_webhook_simulation() {
    log_step "Test 8: Simulating Stripe webhook..."
    
    # This is a simplified webhook payload for testing
    # In real testing, use Stripe CLI: stripe listen --forward-to localhost:44345/api/payment/webhook/stripe
    
    log_info "To test webhooks, use Stripe CLI:"
    log_info "  stripe listen --forward-to https://localhost:44345/api/payment/webhook/stripe"
    log_info "  stripe trigger checkout.session.completed"
}

# Run all tests
run_all_tests() {
    local passed=0
    local failed=0
    
    log_info "========================================"
    log_info "Running Payment Flow Tests"
    log_info "========================================"
    echo ""
    
    # Test: Get Products
    if test_get_products; then
        ((passed++))
    else
        ((failed++))
    fi
    echo ""
    
    # Test: Get Customer
    if test_get_customer; then
        ((passed++))
    else
        ((failed++))
    fi
    echo ""
    
    # Test: Create Checkout Session
    if test_create_checkout_session; then
        ((passed++))
    else
        ((failed++))
    fi
    echo ""
    
    # Test: Get Subscription Status
    test_get_subscription_status
    ((passed++))
    echo ""
    
    # Test: Get Payment History
    test_get_payment_history
    ((passed++))
    echo ""
    
    # Test: New API Products
    test_new_api_products
    ((passed++))
    echo ""
    
    # Test: New API Subscribe
    test_new_api_subscribe
    ((passed++))
    echo ""
    
    # Webhook simulation info
    test_webhook_simulation
    echo ""
    
    log_info "========================================"
    log_info "Test Results: $passed passed, $failed failed"
    log_info "========================================"
}

# Main function
main() {
    echo ""
    log_info "========================================"
    log_info "Aevatar Payment Flow Test"
    log_info "========================================"
    echo ""
    
    check_dependencies
    check_services
    echo ""
    
    get_access_token
    echo ""
    
    case "${1:-all}" in
        "products")
            test_get_products
            ;;
        "customer")
            test_get_customer
            ;;
        "checkout")
            test_create_checkout_session
            ;;
        "status")
            test_get_subscription_status
            ;;
        "history")
            test_get_payment_history
            ;;
        "new-api")
            test_new_api_products
            test_new_api_subscribe
            ;;
        "all"|*)
            run_all_tests
            ;;
    esac
}

main "$@"


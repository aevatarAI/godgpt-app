#!/bin/bash

# =============================================================================
# Test Payment Webhook Flow Script
# Tests payment webhook callbacks for Stripe, Apple, and Google Play
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Load common test utilities
source "$SCRIPT_DIR/test-common.sh"

# Variables
USER_ID=""
SUBSCRIPTION_ID=""
PRICE_ID=""

# Check dependencies
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

# =============================================================================
# Prerequisites: Create subscription first
# =============================================================================

# Step 1: Create checkout session to get subscription
prepare_subscription() {
    log_step "Preparing: Creating subscription for webhook test..."
    
    # Get products first
    local products=$(api_get "/api/godgpt/payment/products")
    PRICE_ID=$(echo "$products" | jq -r '.data[0].priceId // empty' 2>/dev/null)
    
    if [ -z "$PRICE_ID" ] || [ "$PRICE_ID" == "null" ]; then
        log_warn "No price ID found, using default test price"
        PRICE_ID="price_1RPftu4KJpMhj2HtxBbRGXMW"
    fi
    
    log_info "Using price ID: $PRICE_ID"
    
    # Create subscription
    local response=$(api_post "/api/godgpt/payment/create-subscription" "{
        \"priceId\": \"$PRICE_ID\",
        \"devicePlatform\": \"web\"
    }")
    
    log_response "$response"
    
    SUBSCRIPTION_ID=$(echo "$response" | jq -r '.data.subscriptionId // empty' 2>/dev/null)
    
    if [ -n "$SUBSCRIPTION_ID" ] && [ "$SUBSCRIPTION_ID" != "null" ]; then
        log_info "Subscription created: $SUBSCRIPTION_ID"
    else
        log_warn "Could not create subscription (may need payment method)"
    fi
}

# =============================================================================
# Stripe Webhook Tests
# =============================================================================

# Test Stripe invoice.paid webhook (first-time subscription)
test_stripe_webhook_invoice_paid() {
    log_step "Test: Stripe invoice.paid webhook (subscription_create)..."
    
    # Get user ID from token
    local user_info=$(api_get "/api/app/current-user")
    USER_ID=$(echo "$user_info" | jq -r '.data.id // .id // empty' 2>/dev/null)
    
    if [ -z "$USER_ID" ] || [ "$USER_ID" == "null" ]; then
        USER_ID="00000000-0000-0000-0000-000000000001"
        log_warn "Could not get user ID, using test ID: $USER_ID"
    fi
    
    local timestamp=$(date +%s)
    local invoice_id="in_test_${timestamp}"
    local sub_id="${SUBSCRIPTION_ID:-sub_test_${timestamp}}"
    
    # Stripe webhook payload for invoice.paid (first-time)
    local payload=$(cat <<EOF
{
  "id": "evt_test_${timestamp}",
  "object": "event",
  "api_version": "2025-04-30.basil",
  "created": ${timestamp},
  "data": {
    "object": {
      "id": "${invoice_id}",
      "object": "invoice",
      "amount_paid": 2000,
      "amount_due": 2000,
      "billing_reason": "subscription_create",
      "currency": "usd",
      "customer": "cus_test_123",
      "subscription": "${sub_id}",
      "status": "paid",
      "parent": {
        "subscription_details": {
          "subscription": {
            "id": "${sub_id}"
          }
        }
      },
      "lines": {
        "data": [
          {
            "id": "il_test_${timestamp}",
            "pricing": {
              "type": "price_details",
              "price_details": {
                "price": "${PRICE_ID:-price_1RPftu4KJpMhj2HtxBbRGXMW}"
              }
            }
          }
        ]
      },
      "subscription_details": {
        "metadata": {
          "userId": "${USER_ID}"
        }
      },
      "metadata": {
        "userId": "${USER_ID}"
      }
    }
  },
  "type": "invoice.paid"
}
EOF
)

    log_info "Sending Stripe invoice.paid webhook (billing_reason=subscription_create)..."
    log_info "User ID: $USER_ID"
    log_info "Invoice ID: $invoice_id"
    log_info "Subscription ID: $sub_id"
    
    # Signature verification is disabled when WebhookSecret is empty or "test"
    # Set in appsettings.json: "WebhookSecret": "test" or ""
    local response=$(curl -s -X POST "$BASE_URL/api/webhooks/godgpt-stripe-payment" \
        -H "Content-Type: application/json" \
        -d "$payload")
    
    log_response "$response"
    log_info "Stripe invoice.paid webhook test completed"
}

# Test Stripe invoice.paid webhook (renewal)
test_stripe_webhook_renewal() {
    log_step "Test: Stripe invoice.paid webhook (subscription_cycle - renewal)..."
    
    local timestamp=$(date +%s)
    local invoice_id="in_renewal_${timestamp}"
    local sub_id="${SUBSCRIPTION_ID:-sub_test_${timestamp}}"
    
    # Stripe webhook payload for invoice.paid (renewal)
    local payload=$(cat <<EOF
{
  "id": "evt_renewal_${timestamp}",
  "object": "event",
  "api_version": "2025-04-30.basil",
  "created": ${timestamp},
  "data": {
    "object": {
      "id": "${invoice_id}",
      "object": "invoice",
      "amount_paid": 2000,
      "amount_due": 2000,
      "billing_reason": "subscription_cycle",
      "currency": "usd",
      "customer": "cus_test_123",
      "subscription": "${sub_id}",
      "status": "paid",
      "parent": {
        "subscription_details": {
          "subscription": {
            "id": "${sub_id}"
          }
        }
      },
      "lines": {
        "data": [
          {
            "id": "il_renewal_${timestamp}",
            "pricing": {
              "type": "price_details",
              "price_details": {
                "price": "${PRICE_ID:-price_1RPftu4KJpMhj2HtxBbRGXMW}"
              }
            }
          }
        ]
      },
      "subscription_details": {
        "metadata": {
          "userId": "${USER_ID:-00000000-0000-0000-0000-000000000001}"
        }
      },
      "metadata": {
        "userId": "${USER_ID:-00000000-0000-0000-0000-000000000001}"
      }
    }
  },
  "type": "invoice.paid"
}
EOF
)

    log_info "Sending Stripe invoice.paid webhook (billing_reason=subscription_cycle)..."
    log_info "This should be treated as renewal (skip cancel old subscriptions)"
    
    local response=$(curl -s -X POST "$BASE_URL/api/webhooks/godgpt-stripe-payment" \
        -H "Content-Type: application/json" \
        -d "$payload")
    
    log_response "$response"
    log_info "Stripe renewal webhook test completed"
}

# =============================================================================
# Google Play Webhook Tests
# =============================================================================

# Test Google Play INITIAL_PURCHASE webhook
test_google_webhook_initial() {
    log_step "Test: Google Play INITIAL_PURCHASE webhook..."
    
    local timestamp=$(date +%s)
    local transaction_id="GPA.test_${timestamp}"
    
    # RevenueCat webhook payload for Google Play
    local payload=$(cat <<EOF
{
  "api_version": "1.0",
  "event": {
    "type": "INITIAL_PURCHASE",
    "id": "evt_gp_${timestamp}",
    "app_user_id": "${USER_ID:-00000000-0000-0000-0000-000000000001}",
    "original_app_user_id": "${USER_ID:-00000000-0000-0000-0000-000000000001}",
    "product_id": "godgpt_test_premium_monthly:basic",
    "period_type": "NORMAL",
    "purchased_at_ms": ${timestamp}000,
    "expiration_at_ms": $((timestamp + 2592000))000,
    "store": "PLAY_STORE",
    "environment": "PRODUCTION",
    "is_trial_conversion": false,
    "transaction_id": "${transaction_id}",
    "original_transaction_id": "${transaction_id}",
    "price_in_purchased_currency": 20.0,
    "currency": "USD",
    "aliases": ["${USER_ID:-00000000-0000-0000-0000-000000000001}"]
  }
}
EOF
)

    log_info "Sending Google Play INITIAL_PURCHASE webhook..."
    log_info "Product ID: godgpt_test_premium_monthly:basic"
    log_info "IsUltimate: false (Basic product)"
    
    local response=$(curl -s -X POST "$BASE_URL/api/webhooks/godgpt-googleplay-payment" \
        -H "Content-Type: application/json" \
        -H "User-Agent: RevenueCat/1.0" \
        -d "$payload")
    
    log_response "$response"
    log_info "Google Play initial purchase webhook test completed"
}

# Test Google Play RENEWAL webhook
test_google_webhook_renewal() {
    log_step "Test: Google Play RENEWAL webhook..."
    
    local timestamp=$(date +%s)
    local transaction_id="GPA.renewal_${timestamp}"
    
    local payload=$(cat <<EOF
{
  "api_version": "1.0",
  "event": {
    "type": "RENEWAL",
    "id": "evt_gp_renewal_${timestamp}",
    "app_user_id": "${USER_ID:-00000000-0000-0000-0000-000000000001}",
    "original_app_user_id": "${USER_ID:-00000000-0000-0000-0000-000000000001}",
    "product_id": "godgpt_test_premium_monthly:basic",
    "period_type": "NORMAL",
    "purchased_at_ms": ${timestamp}000,
    "expiration_at_ms": $((timestamp + 2592000))000,
    "store": "PLAY_STORE",
    "environment": "PRODUCTION",
    "transaction_id": "${transaction_id}",
    "original_transaction_id": "GPA.original_123",
    "price_in_purchased_currency": 20.0,
    "currency": "USD",
    "aliases": ["${USER_ID:-00000000-0000-0000-0000-000000000001}"]
  }
}
EOF
)

    log_info "Sending Google Play RENEWAL webhook..."
    log_info "This should be treated as renewal (skip cancel old subscriptions)"
    
    local response=$(curl -s -X POST "$BASE_URL/api/webhooks/godgpt-googleplay-payment" \
        -H "Content-Type: application/json" \
        -H "User-Agent: RevenueCat/1.0" \
        -d "$payload")
    
    log_response "$response"
    log_info "Google Play renewal webhook test completed"
}

# Test Google Play Ultimate upgrade
test_google_webhook_ultimate_upgrade() {
    log_step "Test: Google Play Ultimate upgrade webhook..."
    
    local timestamp=$(date +%s)
    local transaction_id="GPA.ultimate_${timestamp}"
    
    local payload=$(cat <<EOF
{
  "api_version": "1.0",
  "event": {
    "type": "INITIAL_PURCHASE",
    "id": "evt_gp_ultimate_${timestamp}",
    "app_user_id": "${USER_ID:-00000000-0000-0000-0000-000000000001}",
    "original_app_user_id": "${USER_ID:-00000000-0000-0000-0000-000000000001}",
    "product_id": "godgpt_test_ultimate_monthly:basic",
    "period_type": "NORMAL",
    "purchased_at_ms": ${timestamp}000,
    "expiration_at_ms": $((timestamp + 2592000))000,
    "store": "PLAY_STORE",
    "environment": "PRODUCTION",
    "transaction_id": "${transaction_id}",
    "original_transaction_id": "${transaction_id}",
    "price_in_purchased_currency": 100.0,
    "currency": "USD",
    "aliases": ["${USER_ID:-00000000-0000-0000-0000-000000000001}"]
  }
}
EOF
)

    log_info "Sending Google Play Ultimate purchase webhook..."
    log_info "Product ID: godgpt_test_ultimate_monthly:basic"
    log_info "IsUltimate: true (should cancel Basic subscriptions)"
    
    local response=$(curl -s -X POST "$BASE_URL/api/webhooks/godgpt-googleplay-payment" \
        -H "Content-Type: application/json" \
        -H "User-Agent: RevenueCat/1.0" \
        -d "$payload")
    
    log_response "$response"
    log_info "Google Play Ultimate upgrade webhook test completed"
}

# =============================================================================
# Apple Webhook Tests
# =============================================================================

# Test Apple SUBSCRIBED webhook
test_apple_webhook_subscribed() {
    log_step "Test: Apple SUBSCRIBED webhook..."
    
    local timestamp=$(date +%s)
    local transaction_id="apple_test_${timestamp}"
    
    # Note: Real Apple webhooks use signed JWS format
    # This is a simplified payload for testing
    local payload=$(cat <<EOF
{
  "notificationType": "SUBSCRIBED",
  "subtype": "INITIAL_BUY",
  "notificationUUID": "uuid_${timestamp}",
  "data": {
    "appAppleId": 123456789,
    "bundleId": "com.gpt.god",
    "bundleVersion": "1.0",
    "environment": "Sandbox",
    "signedTransactionInfo": "eyJhbGciOiJFUzI1NiIsIng1YyI6WyJNSUlDTnpDQ0FkMmdBd0lCQWdJSUFRQUFBQUFBQUFFd0NnWUlLb1pJemowRUF3SXdZekVMTUFrR0ExVUVCaE1DVlZNeEV6QVJCZ05WQkFnVENrTmhiR2xtYjNKdWFXRXhGakFVQmdOVkJBY1REVk5oYmlCR2NtRnVZMmx6WTI4eEZEQVNCZ05WQkFvVEMwMXlMaUJCY0hCc1pTQkpiaTR4RVRBUEJnTlZCQU1UQ0VGd2NHeGxJRWx1WXpBZUZ3MHlNekEyTWpFeE5ETTFNemRhRncweU5EQTJNakF4TkRNMU16ZGFNRGd4TlRBekJnTlZCQU1NTEVGd2NHeGxJRWxFSUNCU2IyOTBJRU5CSUMwZ1J6VXdJRkJ5YjJSMVkzUnBiMjRnUWtORk1qY3dQekFLRmdneEtsdjBYYWd3V0R3NE1JQ0NBUUZCZ01DQkZBd2N3UVFBIl19.eyJ0cmFuc2FjdGlvbklkIjoiJHt0cmFuc2FjdGlvbl9pZH0iLCJvcmlnaW5hbFRyYW5zYWN0aW9uSWQiOiIke3RyYW5zYWN0aW9uX2lkfSIsInByb2R1Y3RJZCI6Im1vbnRobHkyMCIsInB1cmNoYXNlRGF0ZSI6JHt0aW1lc3RhbXB9MDAwLCJleHBpcmVzRGF0ZSI6JCgodGltZXN0YW1wICsgMjU5MjAwMCkpMDAwLCJhcHBBY2NvdW50VG9rZW4iOiIke1VTRVJfSUQ6LTAwMDAwMDAwLTAwMDAtMDAwMC0wMDAwLTAwMDAwMDAwMDAwMX0ifQ.test"
  }
}
EOF
)

    log_info "Sending Apple SUBSCRIBED webhook..."
    log_info "Product ID: monthly20"
    log_info "IsUltimate: false (Basic product)"
    
    local response=$(curl -s -X POST "$BASE_URL/api/webhooks/godgpt-appstore-payment" \
        -H "Content-Type: application/json" \
        -d "$payload")
    
    log_response "$response"
    log_info "Apple SUBSCRIBED webhook test completed"
}

# Test Apple DID_RENEW webhook
test_apple_webhook_renew() {
    log_step "Test: Apple DID_RENEW webhook..."
    
    local timestamp=$(date +%s)
    local transaction_id="apple_renew_${timestamp}"
    
    local payload=$(cat <<EOF
{
  "notificationType": "DID_RENEW",
  "notificationUUID": "uuid_renew_${timestamp}",
  "data": {
    "appAppleId": 123456789,
    "bundleId": "com.gpt.god",
    "bundleVersion": "1.0",
    "environment": "Sandbox",
    "signedTransactionInfo": "eyJhbGciOiJFUzI1NiIsIng1YyI6WyJNSUlDTnpDQ0FkMmdBd0lCQWdJSUFRQUFBQUFBQUFFd0NnWUlLb1pJemowRUF3SXdZekVMTUFrR0ExVUVCaE1DVlZNeEV6QVJCZ05WQkFnVENrTmhiR2xtYjNKdWFXRXhGakFVQmdOVkJBY1REVk5oYmlCR2NtRnVZMmx6WTI4eEZEQVNCZ05WQkFvVEMwMXlMaUJCY0hCc1pTQkpiaTR4RVRBUEJnTlZCQU1UQ0VGd2NHeGxJRWx1WXpBZUZ3MHlNekEyTWpFeE5ETTFNemRhRncweU5EQTJNakF4TkRNMU16ZGFNRGd4TlRBekJnTlZCQU1NTEVGd2NHeGxJRWxFSUNCU2IyOTBJRU5CSUMwZ1J6VXdJRkJ5YjJSMVkzUnBiMjRnUWtORU1qY3dQekFLRmdneEtsdjBYYWd3V0R3NE1JQ0NBUUZCZ01DQkZBd2N3UVFBIl19.eyJ0cmFuc2FjdGlvbklkIjoiJHt0cmFuc2FjdGlvbl9pZH0iLCJvcmlnaW5hbFRyYW5zYWN0aW9uSWQiOiJhcHBsZV9vcmlnaW5hbF8xMjMiLCJwcm9kdWN0SWQiOiJtb250aGx5MjAiLCJwdXJjaGFzZURhdGUiOiR7dGltZXN0YW1wfTAwMCwiZXhwaXJlc0RhdGUiOiQoKHRpbWVzdGFtcCArIDI1OTIwMDApKTAwMCwiYXBwQWNjb3VudFRva2VuIjoiJHtVU0VSX0lEOi0wMDAwMDAwMC0wMDAwLTAwMDAtMDAwMC0wMDAwMDAwMDAwMDF9In0.test"
  }
}
EOF
)

    log_info "Sending Apple DID_RENEW webhook..."
    log_info "This should be treated as renewal (skip cancel old subscriptions)"
    
    local response=$(curl -s -X POST "$BASE_URL/api/webhooks/godgpt-appstore-payment" \
        -H "Content-Type: application/json" \
        -d "$payload")
    
    log_response "$response"
    log_info "Apple DID_RENEW webhook test completed"
}

# =============================================================================
# Run all tests
# =============================================================================

run_all_webhook_tests() {
    local passed=0
    local failed=0
    
    log_info "========================================"
    log_info "Running Payment Webhook Tests"
    log_info "========================================"
    echo ""
    
    # Prepare subscription first
    prepare_subscription
    echo ""
    
    # Stripe tests
    log_info "--- Stripe Webhook Tests ---"
    test_stripe_webhook_invoice_paid
    ((passed++))
    echo ""
    
    test_stripe_webhook_renewal
    ((passed++))
    echo ""
    
    # Google Play tests
    log_info "--- Google Play Webhook Tests ---"
    test_google_webhook_initial
    ((passed++))
    echo ""
    
    test_google_webhook_renewal
    ((passed++))
    echo ""
    
    test_google_webhook_ultimate_upgrade
    ((passed++))
    echo ""
    
    # Apple tests
    log_info "--- Apple Webhook Tests ---"
    test_apple_webhook_subscribed
    ((passed++))
    echo ""
    
    test_apple_webhook_renew
    ((passed++))
    echo ""
    
    log_info "========================================"
    log_info "Webhook Test Results: $passed tests completed"
    log_info "========================================"
    log_info ""
    log_info "Note: Check server logs for detailed processing info"
    log_info "Look for [GodGPTPaymentBusinessService] logs to verify:"
    log_info "  - IsRenewal detection"
    log_info "  - IsUltimate detection"
    log_info "  - Old subscription cancellation"
}

# Main function
main() {
    echo ""
    log_info "========================================"
    log_info "Aevatar Payment Webhook Test"
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
        "stripe")
            prepare_subscription
            test_stripe_webhook_invoice_paid
            test_stripe_webhook_renewal
            ;;
        "stripe-create")
            prepare_subscription
            test_stripe_webhook_invoice_paid
            ;;
        "stripe-renewal")
            test_stripe_webhook_renewal
            ;;
        "google")
            test_google_webhook_initial
            test_google_webhook_renewal
            test_google_webhook_ultimate_upgrade
            ;;
        "google-create")
            test_google_webhook_initial
            ;;
        "google-renewal")
            test_google_webhook_renewal
            ;;
        "google-ultimate")
            test_google_webhook_ultimate_upgrade
            ;;
        "apple")
            test_apple_webhook_subscribed
            test_apple_webhook_renew
            ;;
        "apple-create")
            test_apple_webhook_subscribed
            ;;
        "apple-renewal")
            test_apple_webhook_renew
            ;;
        "all"|*)
            run_all_webhook_tests
            ;;
    esac
}

main "$@"

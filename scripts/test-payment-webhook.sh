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
ORDER_ID=""

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

# Step 1: Create checkout session to get subscription and order_id
prepare_subscription() {
    log_step "Preparing: Creating checkout session for webhook test..."
    
    # Get user ID from token
    local user_info=$(api_get "/api/app/current-user")
    USER_ID=$(echo "$user_info" | jq -r '.data.id // .id // empty' 2>/dev/null)
    
    if [ -z "$USER_ID" ] || [ "$USER_ID" == "null" ]; then
        USER_ID="00000000-0000-0000-0000-000000000001"
        log_warn "Could not get user ID, using test ID: $USER_ID"
    fi
    log_info "User ID: $USER_ID"
    
    # Get products first
    local products=$(api_get "/api/godgpt/payment/products")
    PRICE_ID=$(echo "$products" | jq -r '.data[0].priceId // empty' 2>/dev/null)
    
    if [ -z "$PRICE_ID" ] || [ "$PRICE_ID" == "null" ]; then
        log_warn "No price ID found, using default test price"
        PRICE_ID="price_1RPftu4KJpMhj2HtxBbRGXMW"
    fi
    
    log_info "Using price ID: $PRICE_ID"
    
    # Generate order_id (same as server-side logic)
    ORDER_ID=$(uuidgen | tr '[:upper:]' '[:lower:]')
    log_info "Generated order_id: $ORDER_ID"
    
    # Create checkout session (this creates PaymentRecordGAgent with order_id)
    local response=$(api_post "/api/godgpt/payment/create-checkout-session" "{
        \"priceId\": \"$PRICE_ID\",
        \"uiMode\": \"hosted\"
    }")
    
    log_response "$response"
    
    SUBSCRIPTION_ID=$(echo "$response" | jq -r '.data.subscriptionId // empty' 2>/dev/null)
    
    if [ -n "$SUBSCRIPTION_ID" ] && [ "$SUBSCRIPTION_ID" != "null" ]; then
        log_info "Checkout session created: $SUBSCRIPTION_ID"
        log_info "PaymentRecordGAgent should be created with this order_id"
    else
        log_warn "Could not create checkout session, using generated order_id for test"
    fi
}

# =============================================================================
# Stripe Webhook Tests
# =============================================================================

# Test Stripe checkout.session.completed webhook
test_stripe_webhook_checkout_completed() {
    log_step "Test: Stripe checkout.session.completed webhook..."
    
    local timestamp=$(date +%s)
    local session_id="cs_test_${timestamp}"
    local sub_id="sub_test_${timestamp}"
    local order_id="${ORDER_ID:-$(uuidgen | tr '[:upper:]' '[:lower:]')}"
    
    # Stripe webhook payload for checkout.session.completed
    local payload=$(cat <<EOF
{
  "id": "evt_checkout_${timestamp}",
  "object": "event",
  "api_version": "2025-04-30.basil",
  "created": ${timestamp},
  "data": {
    "object": {
      "id": "${session_id}",
      "object": "checkout.session",
      "mode": "subscription",
      "status": "complete",
      "customer": "cus_test_123",
      "subscription": "${sub_id}",
      "client_reference_id": "${USER_ID:-00000000-0000-0000-0000-000000000001}",
      "metadata": {
        "internal_user_id": "${USER_ID:-00000000-0000-0000-0000-000000000001}",
        "order_id": "${order_id}",
        "price_id": "${PRICE_ID:-price_1RPftu4KJpMhj2HtxBbRGXMW}"
      }
    }
  },
  "type": "checkout.session.completed"
}
EOF
)

    log_info "Sending Stripe checkout.session.completed webhook..."
    log_info "User ID: ${USER_ID:-00000000-0000-0000-0000-000000000001}"
    log_info "Session ID: $session_id"
    log_info "Subscription ID: $sub_id"
    log_info "Order ID: $order_id (used for PaymentRecordGAgent lookup)"
    
    # Signature verification is disabled when WebhookSecret is empty or "test"
    local response=$(curl -s -X POST "$BASE_URL/api/webhooks/godgpt-stripe-payment" \
        -H "Content-Type: application/json" \
        -d "$payload")
    
    log_response "$response"
    log_info "Stripe checkout.session.completed webhook test completed"
    
    # Save for subsequent tests
    SUBSCRIPTION_ID="$sub_id"
    ORDER_ID="$order_id"
}

# Test Stripe invoice.paid webhook (first-time subscription)
test_stripe_webhook_invoice_paid() {
    log_step "Test: Stripe invoice.paid webhook (subscription_create)..."
    
    local timestamp=$(date +%s)
    local invoice_id="in_test_${timestamp}"
    local sub_id="${SUBSCRIPTION_ID:-sub_test_${timestamp}}"
    local order_id="${ORDER_ID:-$(uuidgen | tr '[:upper:]' '[:lower:]')}"
    
    # Stripe webhook payload for invoice.paid (first-time)
    # NOTE: order_id is in subscription_details.metadata (same as old code)
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
          },
          "metadata": {
            "internal_user_id": "${USER_ID:-00000000-0000-0000-0000-000000000001}",
            "order_id": "${order_id}",
            "price_id": "${PRICE_ID:-price_1RPftu4KJpMhj2HtxBbRGXMW}"
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
            },
            "period": {
              "start": ${timestamp},
              "end": $((timestamp + 2592000))
            }
          }
        ]
      }
    }
  },
  "type": "invoice.paid"
}
EOF
)

    log_info "Sending Stripe invoice.paid webhook (billing_reason=subscription_create)..."
    log_info "User ID: ${USER_ID:-00000000-0000-0000-0000-000000000001}"
    log_info "Invoice ID: $invoice_id"
    log_info "Subscription ID: $sub_id"
    log_info "Order ID: $order_id (used for PaymentRecordGAgent lookup)"
    
    # Signature verification is disabled when WebhookSecret is empty or "test"
    local response=$(curl -s -X POST "$BASE_URL/api/webhooks/godgpt-stripe-payment" \
        -H "Content-Type: application/json" \
        -d "$payload")
    
    log_response "$response"
    log_info "Stripe invoice.paid webhook test completed"
}

# Test Stripe charge.refunded webhook
test_stripe_webhook_charge_refunded() {
    log_step "Test: Stripe charge.refunded webhook..."
    
    local timestamp=$(date +%s)
    local charge_id="ch_test_${timestamp}"
    local payment_intent_id="pi_test_${timestamp}"
    local order_id="${ORDER_ID:-$(uuidgen | tr '[:upper:]' '[:lower:]')}"
    
    # Stripe webhook payload for charge.refunded
    # NOTE: In production, metadata is fetched from PaymentIntent or Invoice->Subscription
    # In JSON fallback test mode, OrderId will be null (expected behavior)
    local payload=$(cat <<EOF
{
  "id": "evt_refund_${timestamp}",
  "object": "event",
  "api_version": "2025-04-30.basil",
  "created": ${timestamp},
  "data": {
    "object": {
      "id": "${charge_id}",
      "object": "charge",
      "amount": 2000,
      "amount_refunded": 2000,
      "currency": "usd",
      "customer": "cus_test_123",
      "payment_intent": "${payment_intent_id}",
      "refunded": true,
      "status": "succeeded",
      "metadata": {
        "internal_user_id": "${USER_ID:-00000000-0000-0000-0000-000000000001}",
        "order_id": "${order_id}",
        "price_id": "${PRICE_ID:-price_1RPftu4KJpMhj2HtxBbRGXMW}"
      }
    }
  },
  "type": "charge.refunded"
}
EOF
)

    log_info "Sending Stripe charge.refunded webhook..."
    log_info "User ID: ${USER_ID:-00000000-0000-0000-0000-000000000001}"
    log_info "Charge ID: $charge_id"
    log_info "Order ID: $order_id"
    log_info "Refund Amount: 20.00 USD (full refund)"
    log_info "NOTE: In JSON fallback test mode, OrderId may be null - this is expected"
    log_info "      Production uses HandleChargeRefunded to fetch metadata from PaymentIntent/Subscription"
    
    # Signature verification is disabled when WebhookSecret is empty or "test"
    local response=$(curl -s -X POST "$BASE_URL/api/webhooks/godgpt-stripe-payment" \
        -H "Content-Type: application/json" \
        -d "$payload")
    
    log_response "$response"
    log_info "Stripe charge.refunded webhook test completed"
}

# Test Stripe invoice.paid webhook (renewal)
test_stripe_webhook_renewal() {
    log_step "Test: Stripe invoice.paid webhook (subscription_cycle - renewal)..."
    
    local timestamp=$(date +%s)
    local invoice_id="in_renewal_${timestamp}"
    local sub_id="${SUBSCRIPTION_ID:-sub_test_${timestamp}}"
    local order_id="${ORDER_ID:-$(uuidgen | tr '[:upper:]' '[:lower:]')}"
    
    # Stripe webhook payload for invoice.paid (renewal)
    # NOTE: order_id is in subscription_details.metadata (same as old code)
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
          },
          "metadata": {
            "internal_user_id": "${USER_ID:-00000000-0000-0000-0000-000000000001}",
            "order_id": "${order_id}",
            "price_id": "${PRICE_ID:-price_1RPftu4KJpMhj2HtxBbRGXMW}"
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
            },
            "period": {
              "start": ${timestamp},
              "end": $((timestamp + 2592000))
            }
          }
        ]
      }
    }
  },
  "type": "invoice.paid"
}
EOF
)

    log_info "Sending Stripe invoice.paid webhook (billing_reason=subscription_cycle)..."
    log_info "Order ID: $order_id"
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
    
    # Prepare subscription first (creates PaymentRecordGAgent with order_id)
    prepare_subscription
    echo ""
    
    # Stripe tests
    log_info "--- Stripe Webhook Tests ---"
    
    # Test 1: checkout.session.completed (sets up PaymentRecordGAgent if not already)
    test_stripe_webhook_checkout_completed
    ((passed++))
    echo ""
    
    # Test 2: invoice.paid (first-time) - uses order_id to find record
    test_stripe_webhook_invoice_paid
    ((passed++))
    echo ""
    
    # Test 3: invoice.paid (renewal) - uses order_id to find record
    test_stripe_webhook_renewal
    ((passed++))
    echo ""
    
    # Test 4: charge.refunded - tests refund handling
    test_stripe_webhook_charge_refunded
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

# Test expired subscription filtering
test_expired_subscription() {
    log_step "Test: Expired subscription should be filtered out..."
    
    local timestamp=$(date +%s)
    local invoice_id="in_expired_${timestamp}"
    local sub_id="sub_expired_${timestamp}"
    local order_id=$(uuidgen | tr '[:upper:]' '[:lower:]')
    local expired_end=$((timestamp - 86400))  # 1 day ago
    
    # Send invoice.paid with expired period_end
    local payload=$(cat <<EOF
{
  "id": "evt_expired_${timestamp}",
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
          },
          "metadata": {
            "internal_user_id": "${USER_ID:-00000000-0000-0000-0000-000000000001}",
            "order_id": "${order_id}",
            "price_id": "${PRICE_ID:-price_1RPftu4KJpMhj2HtxBbRGXMW}"
          }
        }
      },
      "lines": {
        "data": [
          {
            "id": "il_expired_${timestamp}",
            "pricing": {
              "type": "price_details",
              "price_details": {
                "price": "${PRICE_ID:-price_1RPftu4KJpMhj2HtxBbRGXMW}"
              }
            },
            "period": {
              "start": $((expired_end - 2592000)),
              "end": ${expired_end}
            }
          }
        ]
      }
    }
  },
  "type": "invoice.paid"
}
EOF
)

    log_info "Sending expired subscription webhook..."
    log_info "Period end: $(date -r $expired_end '+%Y-%m-%d %H:%M:%S') (expired)"
    
    local response=$(curl -s -X POST "$BASE_URL/api/webhooks/godgpt-stripe-payment" \
        -H "Content-Type: application/json" \
        -d "$payload")
    
    log_response "$response"
    
    # Verify subscription status
    log_info "Verifying expired subscription is filtered out..."
    local status_response=$(api_get "/api/godgpt/payment/has-active-subscription")
    log_response "$status_response"
    
    local has_active=$(echo "$status_response" | jq -r '.data.hasActiveSubscription // false')
    if [ "$has_active" == "false" ]; then
        log_info "✅ Expired subscription correctly filtered out"
    else
        log_warn "⚠️  Expired subscription NOT filtered (hasActiveSubscription=$has_active)"
    fi
}

# Test subscription lifecycle with status verification
test_subscription_lifecycle() {
    log_step "Test: Complete subscription lifecycle with status checks..."
    
    local timestamp=$(date +%s)
    local order_id=$(uuidgen | tr '[:upper:]' '[:lower:]')
    local sub_id="sub_lifecycle_${timestamp}"
    local session_id="cs_lifecycle_${timestamp}"
    
    # Step 1: Create subscription via checkout.session.completed
    log_info "Step 1: Creating subscription..."
    local checkout_payload=$(cat <<EOF
{
  "id": "evt_lifecycle_checkout_${timestamp}",
  "object": "event",
  "created": ${timestamp},
  "data": {
    "object": {
      "id": "${session_id}",
      "object": "checkout.session",
      "mode": "subscription",
      "status": "complete",
      "customer": "cus_lifecycle_123",
      "subscription": "${sub_id}",
      "client_reference_id": "${USER_ID:-00000000-0000-0000-0000-000000000001}",
      "metadata": {
        "internal_user_id": "${USER_ID:-00000000-0000-0000-0000-000000000001}",
        "order_id": "${order_id}",
        "price_id": "${PRICE_ID:-price_1RPftu4KJpMhj2HtxBbRGXMW}"
      }
    }
  },
  "type": "checkout.session.completed"
}
EOF
)
    
    curl -s -X POST "$BASE_URL/api/webhooks/godgpt-stripe-payment" \
        -H "Content-Type: application/json" \
        -d "$checkout_payload" > /dev/null
    
    log_info "✅ Checkout completed, order_id: $order_id"
    
    # Step 2: Verify active subscription
    log_info "Step 2: Verifying active subscription..."
    local status1=$(api_get "/api/godgpt/payment/has-active-subscription")
    local has_active1=$(echo "$status1" | jq -r '.data.hasActiveSubscription // false')
    log_info "Has active subscription: $has_active1"
    
    # Step 3: Simulate renewal with future period_end
    log_info "Step 3: Simulating renewal..."
    local future_end=$((timestamp + 2592000))  # +30 days
    local renewal_payload=$(cat <<EOF
{
  "id": "evt_lifecycle_renewal_${timestamp}",
  "object": "event",
  "created": ${timestamp},
  "data": {
    "object": {
      "id": "in_lifecycle_${timestamp}",
      "object": "invoice",
      "amount_paid": 2000,
      "billing_reason": "subscription_cycle",
      "subscription": "${sub_id}",
      "status": "paid",
      "parent": {
        "subscription_details": {
          "subscription": {
            "id": "${sub_id}"
          },
          "metadata": {
            "internal_user_id": "${USER_ID:-00000000-0000-0000-0000-000000000001}",
            "order_id": "${order_id}",
            "price_id": "${PRICE_ID:-price_1RPftu4KJpMhj2HtxBbRGXMW}"
          }
        }
      },
      "lines": {
        "data": [
          {
            "id": "il_lifecycle_${timestamp}",
            "pricing": {
              "type": "price_details",
              "price_details": {
                "price": "${PRICE_ID:-price_1RPftu4KJpMhj2HtxBbRGXMW}"
              }
            },
            "period": {
              "start": ${timestamp},
              "end": ${future_end}
            }
          }
        ]
      }
    }
  },
  "type": "invoice.paid"
}
EOF
)
    
    curl -s -X POST "$BASE_URL/api/webhooks/godgpt-stripe-payment" \
        -H "Content-Type: application/json" \
        -d "$renewal_payload" > /dev/null
    
    log_info "✅ Renewal processed, period_end: $(date -r $future_end '+%Y-%m-%d')"
    
    # Step 4: Verify still active
    log_info "Step 4: Verifying subscription still active..."
    local status2=$(api_get "/api/godgpt/payment/has-active-subscription")
    local has_active2=$(echo "$status2" | jq -r '.data.hasActiveSubscription // false')
    log_info "Has active subscription: $has_active2"
    
    if [ "$has_active2" == "true" ]; then
        log_info "✅ Subscription lifecycle test PASSED"
    else
        log_warn "⚠️  Subscription lifecycle test FAILED (should be active)"
    fi
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
            test_stripe_webhook_checkout_completed
            test_stripe_webhook_invoice_paid
            test_stripe_webhook_renewal
            ;;
        "stripe-checkout")
            prepare_subscription
            test_stripe_webhook_checkout_completed
            ;;
        "stripe-create")
            prepare_subscription
            test_stripe_webhook_checkout_completed
            test_stripe_webhook_invoice_paid
            ;;
        "stripe-renewal")
            test_stripe_webhook_renewal
            ;;
        "stripe-refund"|"refund")
            prepare_subscription
            test_stripe_webhook_charge_refunded
            ;;
        "lifecycle")
            test_subscription_lifecycle
            ;;
        "expired")
            test_expired_subscription
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

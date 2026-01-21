#!/bin/bash

# =============================================================================
# GodGPT Image Chat Flow Test
# Tests image upload and image-based chat functionality
# =============================================================================

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
API_URL="${API_URL:-http://localhost:8082}"
AUTH_URL="${AUTH_URL:-http://localhost:8001}"
CLIENT_ID="App_App"
TEST_USERNAME="admin"
TEST_PASSWORD="1q2w3E*"
SCOPE="Aevatar openid profile"

# Variables
ACCESS_TOKEN=""
SESSION_ID=""
IMAGE_KEY=""

# Logging functions
log_info() { echo -e "${GREEN}[INFO]${NC} $1"; }
log_step() { echo -e "${BLUE}[STEP]${NC} $1"; }
log_warn() { echo -e "${YELLOW}[WARN]${NC} $1"; }
log_error() { echo -e "${RED}[ERROR]${NC} $1"; }
log_response() { echo -e "${YELLOW}[RESPONSE]${NC}\n$1"; }

# =============================================================================
# Authentication
# =============================================================================
login() {
    log_step "Logging in to get access token..."
    
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
}

# =============================================================================
# Create a simple test image (1x1 red PNG)
# =============================================================================
create_test_image() {
    log_step "Creating test image..."
    
    # Base64 of a minimal valid 1x1 red PNG
    local PNG_BASE64="iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg=="
    
    # Decode to file
    echo "$PNG_BASE64" | base64 -d > /tmp/test_image.png
    
    if [ -f /tmp/test_image.png ]; then
        log_info "Test image created: /tmp/test_image.png ($(wc -c < /tmp/test_image.png) bytes)"
    else
        log_error "Failed to create test image"
        exit 1
    fi
}

# =============================================================================
# Upload Image
# =============================================================================
upload_image() {
    log_step "Uploading test image..."
    
    local response=$(curl -k -s -X POST "$API_URL/api/godgpt/blob" \
        -H "Authorization: Bearer $ACCESS_TOKEN" \
        -H "GodgptLanguage: en" \
        -F "file=@/tmp/test_image.png")
    
    log_info "Upload response: $response"
    
    # Check for error code first
    local code=$(echo "$response" | jq -r '.code // "unknown"' 2>/dev/null)
    if [ "$code" != "20000" ]; then
        local message=$(echo "$response" | jq -r '.message // "Unknown error"' 2>/dev/null)
        log_error "Upload failed with code $code: $message"
        log_warn "This may be due to daily upload limits. Try again tomorrow or with premium account."
        exit 1
    fi
    
    # Extract the actual image key from response
    # Response format: {"code":"20000","data":"uuid.png","message":""}
    IMAGE_KEY=$(echo "$response" | jq -r '.data' 2>/dev/null)
    
    if [ -z "$IMAGE_KEY" ] || [ "$IMAGE_KEY" == "null" ]; then
        log_error "Failed to extract image key"
        log_response "$response"
        exit 1
    fi
    
    log_info "Image uploaded successfully!"
    log_info "Image key: $IMAGE_KEY"
}

# =============================================================================
# Create Session
# =============================================================================
create_session() {
    log_step "Creating chat session..."
    
    local response=$(curl -k -s -X POST "$API_URL/api/godgpt/create-session" \
        -H "Authorization: Bearer $ACCESS_TOKEN" \
        -H "Content-Type: application/json" \
        -H "GodgptLanguage: en" \
        -d '{"guider": "", "userLocalTime": "2024-12-23T10:00:00Z"}')
    
    SESSION_ID=$(echo "$response" | jq -r '.data // .sessionId // .')
    SESSION_ID=$(echo "$SESSION_ID" | tr -d '"')
    
    if [ -z "$SESSION_ID" ] || [ "$SESSION_ID" == "null" ]; then
        log_error "Failed to create session"
        log_response "$response"
        exit 1
    fi
    
    log_info "Session created: $SESSION_ID ✓"
}

# =============================================================================
# Send Chat with Image
# =============================================================================
send_chat_with_image() {
    log_step "Sending chat message with image..."
    
    local request_body=$(cat <<EOF
{
    "sessionId": "$SESSION_ID",
    "content": "What do you see in this image? Describe its color and size.",
    "region": null,
    "images": ["$IMAGE_KEY"],
    "userLocalTime": "2024-12-23T10:00:00Z",
    "userTimeZoneId": "UTC"
}
EOF
)
    
    log_info "Request body:"
    echo "$request_body" | jq .
    
    echo ""
    log_step "Waiting for AI response (SSE stream)..."
    
    local response=$(curl -k -s -N -X POST "$API_URL/api/gotgpt/chat" \
        -H "Authorization: Bearer $ACCESS_TOKEN" \
        -H "Content-Type: application/json" \
        -H "Accept: text/event-stream" \
        -H "GodgptLanguage: en" \
        --max-time 60 \
        -d "$request_body" | head -n 30)
    
    echo ""
    log_info "=== SSE Response (first 30 lines) ==="
    echo "$response"
    echo ""
    
    # Check if we got SSE data
    if echo "$response" | grep -q "^data: "; then
        log_info "✅ Image chat SSE stream received!"
        
        # Extract and display the full response text
        echo ""
        log_info "=== AI Response Text ==="
        echo "$response" | grep "^data:" | while read line; do
            echo "$line" | sed 's/^data: //' | jq -r '.Response // empty' 2>/dev/null | tr -d '\n'
        done
        echo ""
        echo ""
        
        # Check for error
        if echo "$response" | grep -q '"ErrorCode":0'; then
            log_info "✅ No error in response"
        else
            local error_code=$(echo "$response" | grep -o '"ErrorCode":[0-9]*' | head -1 | cut -d: -f2)
            if [ -n "$error_code" ] && [ "$error_code" != "0" ]; then
                log_warn "Response contains error code: $error_code"
            fi
        fi
    else
        log_error "❌ No SSE data received"
        log_response "$response"
        return 1
    fi
}

# =============================================================================
# Cleanup
# =============================================================================
cleanup() {
    log_step "Cleaning up..."
    rm -f /tmp/test_image.png
    log_info "Cleanup completed"
}

# =============================================================================
# Main
# =============================================================================
main() {
    echo ""
    log_info "========================================"
    log_info "GodGPT Image Chat Flow Test"
    log_info "========================================"
    echo ""
    log_info "API_URL:  $API_URL"
    log_info "AUTH_URL: $AUTH_URL"
    echo ""
    
    login
    create_test_image
    upload_image
    create_session
    send_chat_with_image
    cleanup
    
    echo ""
    log_info "========================================"
    log_info "✅ Image Chat Test Completed!"
    log_info "========================================"
}

# Run main
main "$@"

#!/bin/bash

# =============================================================================
# Script to add systemPromptManager role to admin user
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
if ! command -v jq &> /dev/null; then
    log_error "jq is required but not installed. Install with: brew install jq"
    exit 1
fi

# Check if services are running
log_step "Checking if services are running..."
if ! curl -k -s "$AUTH_URL/.well-known/openid-configuration" > /dev/null 2>&1; then
    log_error "AuthServer is not running at $AUTH_URL"
    exit 1
fi
log_info "AuthServer is running ✓"

# Get access token
log_step "Getting access token..."
ACCESS_TOKEN=$(curl -k -s -X POST "$AUTH_URL/connect/token" \
    -H "Content-Type: application/x-www-form-urlencoded" \
    -d "grant_type=password" \
    -d "client_id=$CLIENT_ID" \
    -d "username=$TEST_USERNAME" \
    -d "password=$TEST_PASSWORD" \
    -d "scope=$SCOPE" \
    | jq -r '.access_token')

if [ "$ACCESS_TOKEN" == "null" ] || [ -z "$ACCESS_TOKEN" ]; then
    log_error "Failed to get access token"
    exit 1
fi
log_info "Access token obtained ✓"

# Get admin user ID
log_step "Getting admin user ID..."
USER_RESPONSE=$(curl -k -s -X GET "$API_URL/api/identity/users?Filter=admin" \
    -H "Authorization: Bearer $ACCESS_TOKEN" \
    -H "Content-Type: application/json")

ADMIN_USER_ID=$(echo "$USER_RESPONSE" | jq -r '.items[0].id // empty')

if [ -z "$ADMIN_USER_ID" ] || [ "$ADMIN_USER_ID" == "null" ]; then
    log_error "Failed to find admin user"
    log_response "$USER_RESPONSE"
    exit 1
fi
log_info "Found admin user ID: $ADMIN_USER_ID"

# Check if systemPromptManager role exists, create if not
log_step "Checking if systemPromptManager role exists..."
ROLES_RESPONSE=$(curl -k -s -X GET "$API_URL/api/identity/roles" \
    -H "Authorization: Bearer $ACCESS_TOKEN" \
    -H "Content-Type: application/json")

ROLE_ID=$(echo "$ROLES_RESPONSE" | jq -r '.items[] | select(.name == "systemPromptManager") | .id' | head -1)

if [ -z "$ROLE_ID" ] || [ "$ROLE_ID" == "null" ]; then
    log_step "Creating systemPromptManager role..."
    CREATE_ROLE_RESPONSE=$(curl -k -s -X POST "$API_URL/api/identity/roles" \
        -H "Authorization: Bearer $ACCESS_TOKEN" \
        -H "Content-Type: application/json" \
        -d '{
            "name": "systemPromptManager",
            "isDefault": false,
            "isPublic": true
        }')
    
    ROLE_ID=$(echo "$CREATE_ROLE_RESPONSE" | jq -r '.id // empty')
    
    if [ -z "$ROLE_ID" ] || [ "$ROLE_ID" == "null" ]; then
        log_error "Failed to create systemPromptManager role"
        log_response "$CREATE_ROLE_RESPONSE"
        exit 1
    fi
    log_info "Created systemPromptManager role with ID: $ROLE_ID"
else
    log_info "systemPromptManager role already exists with ID: $ROLE_ID"
fi

# Assign role to admin user
log_step "Assigning systemPromptManager role to admin user..."
ASSIGN_RESPONSE=$(curl -k -s -X PUT "$API_URL/api/identity/users/$ADMIN_USER_ID/roles" \
    -H "Authorization: Bearer $ACCESS_TOKEN" \
    -H "Content-Type: application/json" \
    -d "{
        \"roleNames\": [\"admin\", \"systemPromptManager\"]
    }")

log_response "$ASSIGN_RESPONSE"

# Verify role assignment
log_step "Verifying role assignment..."
USER_ROLES_RESPONSE=$(curl -k -s -X GET "$API_URL/api/identity/users/$ADMIN_USER_ID" \
    -H "Authorization: Bearer $ACCESS_TOKEN" \
    -H "Content-Type: application/json")

log_response "$USER_ROLES_RESPONSE"

USER_ROLES=$(echo "$USER_ROLES_RESPONSE" | jq -r '.roleNames[]? // empty' 2>/dev/null | grep -i "systemPromptManager" || echo "")

if [ -n "$USER_ROLES" ]; then
    log_info "✓ Successfully assigned systemPromptManager role to admin user"
    ALL_ROLES=$(echo "$USER_ROLES_RESPONSE" | jq -r '.roleNames | join(", ")' 2>/dev/null || echo "unknown")
    log_info "Admin user now has roles: $ALL_ROLES"
else
    log_warn "Role assignment verification failed. Please check the response above."
    log_warn "You may need to re-login to get the new role in your token."
fi

log_info "Done! Please re-login to get the new role in your token."


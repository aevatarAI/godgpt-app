#!/bin/bash

# =============================================================================
# Common Test Utilities
# Shared functions for all test scripts
# =============================================================================

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Default configuration (can be overridden by environment variables)
AUTH_URL="${AUTH_URL:-https://localhost:44320}"
API_URL="${API_URL:-https://localhost:44345}"
CLIENT_ID="${CLIENT_ID:-AevatarAuthServer}"
SCOPE="${SCOPE:-Aevatar openid profile}"
TEST_USERNAME="${TEST_USERNAME:-admin}"
TEST_PASSWORD="${TEST_PASSWORD:-1q2w3E*}"

# Global variables
ACCESS_TOKEN=""

# =============================================================================
# Logging Functions
# =============================================================================

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

# =============================================================================
# URL Detection
# =============================================================================

probe_url() {
    local url="$1"
    local path="$2"
    # Treat any HTTP response as reachable. We only want to detect correct base URL/port.
    # Use short timeout to avoid hanging.
    curl -k -s --max-time 2 -o /dev/null "$url$path" 2>/dev/null
}

detect_urls() {
    log_step "Detecting API/AUTH base URLs..."

    # Prefer HTTPS defaults, fallback to local HTTP dev ports.
    local api_candidates=("https://localhost:44345" "http://localhost:8082")
    local auth_candidates=("https://localhost:44320" "http://localhost:8001")

    # Only auto-detect if using default values (user can override via env vars)
    if [ "${API_URL:-}" == "https://localhost:44345" ] || [ -z "${API_URL:-}" ]; then
        for candidate in "${api_candidates[@]}"; do
            if probe_url "$candidate" "/api/godgpt/app-config/version"; then
                API_URL="$candidate"
                break
            fi
        done
    fi

    if [ "${AUTH_URL:-}" == "https://localhost:44320" ] || [ -z "${AUTH_URL:-}" ]; then
        for candidate in "${auth_candidates[@]}"; do
            if probe_url "$candidate" "/.well-known/openid-configuration"; then
                AUTH_URL="$candidate"
                break
            fi
        done
    fi

    log_info "Using API_URL:  $API_URL"
    log_info "Using AUTH_URL: $AUTH_URL"
}

# =============================================================================
# Service Health Check
# =============================================================================

check_services() {
    log_step "Checking if services are running..."
    
    local auth_ok=false
    local api_ok=false
    
    # Check AuthServer
    if probe_url "$AUTH_URL" "/.well-known/openid-configuration"; then
        auth_ok=true
    else
        log_error "AuthServer is not running at $AUTH_URL"
    fi
    
    # Check HttpApi
    if probe_url "$API_URL" "/api/godgpt/app-config/version"; then
        api_ok=true
    else
        log_error "HttpApi is not running at $API_URL"
    fi
    
    if [ "$auth_ok" = false ] || [ "$api_ok" = false ]; then
        exit 1
    fi
    
    log_info "All services are running ✓"
}

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
    
    ACCESS_TOKEN=$(echo "$response" | jq -r '.access_token' 2>/dev/null)
    
    if [ "$ACCESS_TOKEN" == "null" ] || [ -z "$ACCESS_TOKEN" ]; then
        log_error "Failed to get access token"
        log_response "$response"
        return 1
    fi
    
    log_info "Access token obtained ✓"
    return 0
}

# =============================================================================
# HTTP Request Helpers
# =============================================================================

# Make authenticated GET request
# extra_headers can be multiple headers separated by newline or semicolon
api_get() {
    local endpoint="$1"
    local extra_headers="${2:-}"
    
    local headers=("-H" "Authorization: Bearer $ACCESS_TOKEN")
    if [ -n "$extra_headers" ]; then
        # Support multiple headers separated by newline or semicolon
        IFS=$'\n' read -ra HEADER_ARRAY <<< "$(echo "$extra_headers" | tr ';' '\n')"
        for header in "${HEADER_ARRAY[@]}"; do
            header=$(echo "$header" | xargs)  # Trim whitespace
            if [ -n "$header" ]; then
                headers+=("-H" "$header")
            fi
        done
    fi
    
    curl -k -s -X GET "$API_URL$endpoint" "${headers[@]}"
}

# Make authenticated POST request
# extra_headers can be multiple headers separated by newline or semicolon
api_post() {
    local endpoint="$1"
    local data="$2"
    local extra_headers="${3:-}"
    
    local headers=("-H" "Authorization: Bearer $ACCESS_TOKEN" "-H" "Content-Type: application/json")
    if [ -n "$extra_headers" ]; then
        # Support multiple headers separated by newline or semicolon
        IFS=$'\n' read -ra HEADER_ARRAY <<< "$(echo "$extra_headers" | tr ';' '\n')"
        for header in "${HEADER_ARRAY[@]}"; do
            header=$(echo "$header" | xargs)  # Trim whitespace
            if [ -n "$header" ]; then
                headers+=("-H" "$header")
            fi
        done
    fi
    
    curl -k -s -X POST "$API_URL$endpoint" "${headers[@]}" -d "$data"
}

# Make authenticated PUT request
# extra_headers can be multiple headers separated by newline or semicolon
api_put() {
    local endpoint="$1"
    local data="$2"
    local extra_headers="${3:-}"
    
    local headers=("-H" "Authorization: Bearer $ACCESS_TOKEN" "-H" "Content-Type: application/json")
    if [ -n "$extra_headers" ]; then
        # Support multiple headers separated by newline or semicolon
        IFS=$'\n' read -ra HEADER_ARRAY <<< "$(echo "$extra_headers" | tr ';' '\n')"
        for header in "${HEADER_ARRAY[@]}"; do
            header=$(echo "$header" | xargs)  # Trim whitespace
            if [ -n "$header" ]; then
                headers+=("-H" "$header")
            fi
        done
    fi
    
    curl -k -s -X PUT "$API_URL$endpoint" "${headers[@]}" -d "$data"
}

# Make authenticated DELETE request
api_delete() {
    local endpoint="$1"
    local extra_headers="${2:-}"
    
    local headers=("-H" "Authorization: Bearer $ACCESS_TOKEN")
    if [ -n "$extra_headers" ]; then
        headers+=("-H" "$extra_headers")
    fi
    
    curl -k -s -X DELETE "$API_URL$endpoint" "${headers[@]}"
}

# Make unauthenticated POST request (for guest endpoints)
# extra_headers can be multiple headers separated by newline or semicolon
api_post_guest() {
    local endpoint="$1"
    local data="$2"
    local extra_headers="${3:-}"
    
    local headers=("-H" "Content-Type: application/json")
    if [ -n "$extra_headers" ]; then
        # Support multiple headers separated by newline or semicolon
        IFS=$'\n' read -ra HEADER_ARRAY <<< "$(echo "$extra_headers" | tr ';' '\n')"
        for header in "${HEADER_ARRAY[@]}"; do
            header=$(echo "$header" | xargs)  # Trim whitespace
            if [ -n "$header" ]; then
                headers+=("-H" "$header")
            fi
        done
    fi
    
    curl -k -s -X POST "$API_URL$endpoint" "${headers[@]}" -d "$data"
}

# Make unauthenticated GET request
# extra_headers can be multiple headers separated by newline or semicolon
api_get_guest() {
    local endpoint="$1"
    local extra_headers="${2:-}"
    
    local headers=()
    if [ -n "$extra_headers" ]; then
        # Support multiple headers separated by newline or semicolon
        IFS=$'\n' read -ra HEADER_ARRAY <<< "$(echo "$extra_headers" | tr ';' '\n')"
        for header in "${HEADER_ARRAY[@]}"; do
            header=$(echo "$header" | xargs)  # Trim whitespace
            if [ -n "$header" ]; then
                headers+=("-H" "$header")
            fi
        done
    fi
    
    curl -k -s -X GET "$API_URL$endpoint" "${headers[@]}"
}

# =============================================================================
# Initialization
# =============================================================================

# Initialize common test environment
init_test() {
    # Source this script if not already sourced
    if [ "${BASH_SOURCE[0]}" != "${0}" ]; then
        # Script is being sourced
        detect_urls
    else
        # Script is being executed directly
        detect_urls
    fi
}

# Auto-initialize if script is sourced
if [ "${BASH_SOURCE[0]}" != "${0}" ]; then
    detect_urls
fi


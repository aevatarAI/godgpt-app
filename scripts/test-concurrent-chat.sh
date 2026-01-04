#!/bin/bash

# =============================================================================
# GodGPT Concurrent Chat Test - Tests Orleans Grain timeout scenarios
# Simulates the non-reentrant grain blocking issue by sending concurrent requests
# =============================================================================

set -e

# Configuration
API_URL="${API_URL:-https://localhost:44345}"
AUTH_URL="${AUTH_URL:-https://localhost:44320}"
CLIENT_ID="AevatarAuthServer"
TEST_USERNAME="admin"
TEST_PASSWORD="1q2w3E*"
SCOPE="Aevatar openid profile"

# Test settings - increase these to simulate timeout scenarios
CONCURRENT_REQUESTS="${CONCURRENT_REQUESTS:-3}"
STRESS_CONCURRENT="${STRESS_CONCURRENT:-6}"
COMPLEX_PROMPT="${COMPLEX_PROMPT:-Write a 2000 word essay about quantum computing with detailed technical explanations.}"
# Very long prompt to trigger slow LLM responses
EXTRA_LONG_PROMPT="${EXTRA_LONG_PROMPT:-Write a comprehensive 5000 word technical analysis comparing quantum computing architectures. Include detailed explanations of superconducting qubits, trapped ions, topological qubits, and photonic quantum computing. Discuss error correction codes, quantum gates, entanglement, and decoherence. Compare IBM Q System, Google Sycamore, IonQ, and Rigetti systems. Include code examples in Qiskit and Cirq.}"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

log_info() { echo -e "${GREEN}[INFO]${NC} $1"; }
log_error() { echo -e "${RED}[ERROR]${NC} $1"; }
log_step() { echo -e "${BLUE}[STEP]${NC} $1"; }

ACCESS_TOKEN=""
SESSION_ID=""

# =============================================================================
# Helper Functions
# =============================================================================

probe_url() {
    local url="$1"
    local path="$2"
    curl -k -s --max-time 2 -o /dev/null "$url$path" 2>/dev/null
}

detect_urls() {
    log_step "Detecting API/AUTH base URLs..."
    local api_candidates=("https://localhost:44345" "http://localhost:8082")
    local auth_candidates=("https://localhost:44320" "http://localhost:8001")

    if [ "${API_URL:-}" == "https://localhost:44345" ]; then
        for candidate in "${api_candidates[@]}"; do
            if probe_url "$candidate" "/api/godgpt/app-config/version"; then
                API_URL="$candidate"
                break
            fi
        done
    fi

    if [ "${AUTH_URL:-}" == "https://localhost:44320" ]; then
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

login() {
    log_step "Logging in to get access token..."
    
    local response=$(curl -k -s -X POST "$AUTH_URL/connect/token" \
        -H "Content-Type: application/x-www-form-urlencoded" \
        -d "grant_type=password" \
        -d "client_id=$CLIENT_ID" \
        -d "username=$TEST_USERNAME" \
        -d "password=$TEST_PASSWORD" \
        -d "scope=$SCOPE")
    
    ACCESS_TOKEN=$(echo "$response" | jq -r '.access_token // empty')
    
    if [ -z "$ACCESS_TOKEN" ]; then
        log_error "Failed to get access token"
        echo "Response: $response"
        exit 1
    fi
    
    log_info "Access token obtained ✓"
}

create_session() {
    log_step "Creating chat session..."
    
    local response=$(curl -k -s -X POST "$API_URL/api/godgpt/create-session" \
        -H "Authorization: Bearer $ACCESS_TOKEN" \
        -H "Content-Type: application/json" \
        -H "GodgptLanguage: en" \
        -d '{"guider": "", "userLocalTime": "2024-12-23T10:00:00Z"}')
    
    # Response format: {"code":"20000","data":"guid-string","message":""}
    SESSION_ID=$(echo "$response" | jq -r '.data // empty')
    
    if [ -z "$SESSION_ID" ] || [ "$SESSION_ID" == "null" ]; then
        log_error "Failed to create session"
        echo "Response: $response"
        exit 1
    fi
    
    log_info "Session created: $SESSION_ID ✓"
}

# =============================================================================
# Test: Single Long-Running Request
# =============================================================================

test_single_long_request() {
    log_step "Test 1: Single long-running request..."
    log_info "Sending complex prompt that takes a long time to process..."
    
    local start_time=$(date +%s)
    
    local response=$(curl -k -s --max-time 120 -X POST "$API_URL/api/gotgpt/chat" \
        -H "Authorization: Bearer $ACCESS_TOKEN" \
        -H "Content-Type: application/json" \
        -H "GodgptLanguage: en" \
        -d "{\"sessionId\": \"$SESSION_ID\", \"content\": \"$COMPLEX_PROMPT\"}" 2>&1)
    
    local end_time=$(date +%s)
    local duration=$((end_time - start_time))
    
    log_info "Request completed in ${duration}s"
    
    # Check if response contains SSE data
    if echo "$response" | grep -q "data:"; then
        log_info "Received SSE stream ✓"
        # Show last few lines
        echo "$response" | tail -5
    else
        log_error "No SSE data received"
        echo "$response" | head -10
    fi
}

# =============================================================================
# Test: Concurrent Requests (Simulates Grain Blocking)
# =============================================================================

send_chat_request() {
    local request_id="$1"
    local prompt="$2"
    local output_file="$3"
    
    echo "[Request $request_id] Starting at $(date +%H:%M:%S)"
    
    local start_time=$(date +%s)
    
    curl -k -s --max-time 60 -X POST "$API_URL/api/gotgpt/chat" \
        -H "Authorization: Bearer $ACCESS_TOKEN" \
        -H "Content-Type: application/json" \
        -H "GodgptLanguage: en" \
        -d "{\"sessionId\": \"$SESSION_ID\", \"content\": \"$prompt\"}" > "$output_file" 2>&1
    
    local end_time=$(date +%s)
    local duration=$((end_time - start_time))
    local exit_code=$?
    
    echo "[Request $request_id] Finished at $(date +%H:%M:%S) (duration: ${duration}s, exit: $exit_code)"
    
    if [ -s "$output_file" ] && grep -q "data:" "$output_file"; then
        echo "[Request $request_id] ✓ Received SSE data"
    elif grep -q "TimeoutException\|timeout\|timed out" "$output_file"; then
        echo "[Request $request_id] ✗ TIMEOUT ERROR DETECTED!"
        head -5 "$output_file"
    else
        echo "[Request $request_id] ? Unknown response"
        head -3 "$output_file"
    fi
}

test_concurrent_requests() {
    log_step "Test 2: Concurrent requests to SAME session (simulates grain blocking)..."
    log_info "Sending $CONCURRENT_REQUESTS concurrent requests to session $SESSION_ID"
    log_info "This tests the non-reentrant grain blocking scenario..."
    
    local temp_dir=$(mktemp -d)
    local pids=()
    
    # Send concurrent requests
    for i in $(seq 1 $CONCURRENT_REQUESTS); do
        local prompt="Request $i: What is $i + $i? Give a brief answer."
        send_chat_request "$i" "$prompt" "$temp_dir/response_$i.txt" &
        pids+=($!)
        sleep 0.1  # Small delay between launches
    done
    
    log_info "Waiting for all requests to complete..."
    
    # Wait for all background processes
    local failed=0
    for pid in "${pids[@]}"; do
        wait $pid || ((failed++))
    done
    
    log_info "========================================"
    log_info "Results Summary:"
    log_info "========================================"
    
    for i in $(seq 1 $CONCURRENT_REQUESTS); do
        local file="$temp_dir/response_$i.txt"
        if [ -f "$file" ]; then
            local size=$(wc -c < "$file")
            if grep -q "TimeoutException\|Response did not arrive" "$file"; then
                log_error "Request $i: TIMEOUT (${size} bytes)"
            elif grep -q "data:" "$file"; then
                log_info "Request $i: SUCCESS (${size} bytes)"
            else
                log_info "Request $i: UNKNOWN (${size} bytes)"
            fi
        fi
    done
    
    # Cleanup
    rm -rf "$temp_dir"
    
    if [ $failed -gt 0 ]; then
        log_error "$failed requests failed/timed out"
    else
        log_info "All requests completed"
    fi
}

# =============================================================================
# Test: Rapid Sequential Requests
# =============================================================================

test_rapid_sequential() {
    log_step "Test 3: Rapid sequential requests (stress test)..."
    
    local success=0
    local failed=0
    
    for i in {1..5}; do
        log_info "Sending request $i/5..."
        
        local response=$(curl -k -s --max-time 45 -X POST "$API_URL/api/gotgpt/chat" \
            -H "Authorization: Bearer $ACCESS_TOKEN" \
            -H "Content-Type: application/json" \
            -H "GodgptLanguage: en" \
            -d "{\"sessionId\": \"$SESSION_ID\", \"content\": \"What is $i times $i?\"}" 2>&1)
        
        if echo "$response" | grep -q "TimeoutException\|Response did not arrive"; then
            log_error "Request $i: TIMEOUT!"
            ((failed++))
        elif echo "$response" | grep -q "data:"; then
            log_info "Request $i: Success ✓"
            ((success++))
        else
            log_error "Request $i: Unknown response"
            ((failed++))
        fi
        
        sleep 0.5  # Brief pause between requests
    done
    
    log_info "Results: $success success, $failed failed"
}

# =============================================================================
# Test: Extreme Stress Test (intentionally triggers timeout)
# =============================================================================

test_extreme_stress() {
    log_step "Test 4: EXTREME stress test ($STRESS_CONCURRENT concurrent long prompts)..."
    log_info "This test intentionally tries to trigger timeout by:"
    log_info "  - Sending $STRESS_CONCURRENT concurrent requests"
    log_info "  - Using extra-long prompts that take 30+ seconds to process"
    log_info "  - All to the SAME session (same grain)"
    echo ""
    
    local temp_dir=$(mktemp -d)
    local pids=()
    
    # Send concurrent LONG requests
    for i in $(seq 1 $STRESS_CONCURRENT); do
        send_chat_request "$i" "$EXTRA_LONG_PROMPT (Variation $i)" "$temp_dir/response_$i.txt" &
        pids+=($!)
        sleep 0.05  # Minimal delay
    done
    
    log_info "Waiting for all $STRESS_CONCURRENT requests (may take 60+ seconds)..."
    
    local failed=0
    local timeout_count=0
    for pid in "${pids[@]}"; do
        wait $pid || ((failed++))
    done
    
    log_info "========================================"
    log_info "Extreme Stress Results:"
    log_info "========================================"
    
    for i in $(seq 1 $STRESS_CONCURRENT); do
        local file="$temp_dir/response_$i.txt"
        if [ -f "$file" ]; then
            local size=$(wc -c < "$file")
            if grep -q "TimeoutException\|Response did not arrive\|timed out" "$file"; then
                log_error "Request $i: TIMEOUT (${size} bytes) <<<< REPRODUCED THE ISSUE"
                ((timeout_count++))
            elif grep -q "data:" "$file"; then
                log_info "Request $i: SUCCESS (${size} bytes)"
            else
                log_info "Request $i: UNKNOWN (${size} bytes)"
            fi
        fi
    done
    
    rm -rf "$temp_dir"
    
    if [ $timeout_count -gt 0 ]; then
        log_error "========================================="
        log_error "TIMEOUT ISSUE REPRODUCED: $timeout_count requests timed out"
        log_error "This confirms the non-reentrant grain blocking problem"
        log_error "========================================="
    else
        log_info "No timeouts occurred in this run"
        log_info "Try increasing STRESS_CONCURRENT or using slower LLM"
    fi
}

# =============================================================================
# Main
# =============================================================================

main() {
    echo "=========================================="
    echo "GodGPT Concurrent Chat Test"
    echo "Tests Orleans Grain Timeout Scenarios"
    echo "=========================================="
    echo ""
    
    detect_urls
    login
    create_session
    
    echo ""
    log_info "Session ID: $SESSION_ID"
    echo ""
    
    # Run tests
    test_single_long_request
    echo ""
    
    test_concurrent_requests
    echo ""
    
    test_rapid_sequential
    echo ""
    
    # Only run extreme test if explicitly requested
    if [ "${RUN_EXTREME_TEST:-0}" == "1" ]; then
        test_extreme_stress
        echo ""
    else
        log_info "Skipping extreme stress test. Set RUN_EXTREME_TEST=1 to run it."
    fi
    
    log_info "=========================================="
    log_info "All tests completed"
    log_info "=========================================="
}

main "$@"


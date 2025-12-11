#!/bin/bash

# =============================================================================
# Start Services Script
# Starts AuthServer, Silo, and HttpApi for testing
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
APP_DIR="$PROJECT_ROOT/apps/Aevatar.App/src"

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

# PID file location
PID_DIR="$SCRIPT_DIR/.pids"
mkdir -p "$PID_DIR"

log_info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

log_warn() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Check if service is already running
check_port() {
    local port=$1
    if lsof -i :$port > /dev/null 2>&1; then
        return 0  # Port is in use
    fi
    return 1  # Port is free
}

# Start a service
start_service() {
    local name=$1
    local dir=$2
    local port=$3
    local project_file=$4
    
    log_info "Starting $name on port $port..."
    
    if check_port $port; then
        log_warn "$name might already be running on port $port"
    fi
    
    cd "$dir"
    
    # Run in background and save PID
    ASPNETCORE_ENVIRONMENT=Development dotnet run --project "$project_file" --no-build > "$PID_DIR/${name}.log" 2>&1 &
    local pid=$!
    echo $pid > "$PID_DIR/${name}.pid"
    
    log_info "$name started with PID $pid"
    
    # Wait a bit for the service to start
    sleep 2
}

# Main function
main() {
    log_info "========================================"
    log_info "Starting Aevatar Services"
    log_info "========================================"
    
    # Check if MongoDB is running
    if ! check_port 27017; then
        log_error "MongoDB is not running on port 27017. Please start MongoDB first."
        exit 1
    fi
    log_info "MongoDB is running ✓"
    
    # Build all projects first
    log_info "Building projects..."
    cd "$PROJECT_ROOT"
    dotnet build apps/Aevatar.App/Aevatar.App.sln --configuration Debug
    
    # 1. Start Silo first (Orleans)
    start_service "silo" "$APP_DIR" "11111" "Aevatar.Silo/Aevatar.Silo.csproj"
    
    # Wait for Silo to be ready
    log_info "Waiting for Silo to be ready..."
    sleep 5
    
    # 2. Start AuthServer
    start_service "auth" "$APP_DIR" "44320" "Aevatar.AuthServer/Aevatar.AuthServer.csproj"
    
    # Wait for AuthServer
    sleep 3
    
    # 3. Start HttpApi
    start_service "httpapi" "$APP_DIR" "44345" "Aevatar.App.HttpApi.Host/Aevatar.App.HttpApi.Host.csproj"
    
    log_info "========================================"
    log_info "All services started!"
    log_info "========================================"
    log_info ""
    log_info "Services:"
    log_info "  - Silo:      Running (Port 11111, Gateway 30000)"
    log_info "  - AuthServer: https://localhost:44320"
    log_info "  - HttpApi:    https://localhost:44345"
    log_info ""
    log_info "Logs:"
    log_info "  - Silo:      $PID_DIR/silo.log"
    log_info "  - AuthServer: $PID_DIR/auth.log"
    log_info "  - HttpApi:    $PID_DIR/httpapi.log"
    log_info ""
    log_info "Use './stop-services.sh' to stop all services"
}

main "$@"


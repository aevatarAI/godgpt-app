#!/bin/bash

# Benchmark Comparison Script for Orleans vs MassTransit
# Based on MASSTRANSIT_INTEGRATION_GUIDE.md

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR/PerformanceTests"

echo "🚀 Aevatar Agent Framework - Performance Benchmark Comparison"
echo "=============================================================="
echo ""
echo "This script will run performance tests for both:"
echo "  1. Orleans Stream Provider"
echo "  2. MassTransit Stream Provider"
echo ""
echo "Prerequisites:"
echo "  - MongoDB running on localhost:27017"
echo "  - Orleans Silo running on localhost:30000"
echo "  - Kafka running on localhost:9092"
echo ""
read -p "Press Enter to continue or Ctrl+C to cancel..."

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Function to run benchmark
run_benchmark() {
    local provider=$1
    local test_type=$2
    
    echo ""
    echo "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
    echo "${GREEN}Testing: ${provider} Provider${NC}"
    echo "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
    echo ""
    
    if [ "$test_type" == "multitopic" ]; then
        dotnet run -- --provider=$provider multitopic
    else
        dotnet run -- --provider=$provider
    fi
}

# Run standard benchmarks
echo ""
echo "${YELLOW}════════════════════════════════════════════════════════════════════════════════════${NC}"
echo "${YELLOW}  PHASE 1: Standard Performance Tests${NC}"
echo "${YELLOW}════════════════════════════════════════════════════════════════════════════════════${NC}"

echo ""
echo "1️⃣  Running Orleans Stream benchmark..."
run_benchmark "Orleans" "standard"

echo ""
echo "⏳ Waiting 5 seconds before next test..."
sleep 5

echo ""
echo "2️⃣  Running MassTransit Stream benchmark..."
run_benchmark "MassTransit" "standard"

# Run multi-topic benchmarks
echo ""
echo "${YELLOW}════════════════════════════════════════════════════════════════════════════════════${NC}"
echo "${YELLOW}  PHASE 2: Multi-Topic Comparison Tests${NC}"
echo "${YELLOW}════════════════════════════════════════════════════════════════════════════════════${NC}"

echo ""
echo "3️⃣  Running Orleans Multi-Topic benchmark..."
run_benchmark "Orleans" "multitopic"

echo ""
echo "⏳ Waiting 5 seconds before next test..."
sleep 5

echo ""
echo "4️⃣  Running MassTransit Multi-Topic benchmark..."
run_benchmark "MassTransit" "multitopic"

echo ""
echo "${GREEN}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo "${GREEN}✅ All benchmarks completed!${NC}"
echo "${GREEN}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo ""
echo "📊 Summary:"
echo "  Compare the throughput (msg/s) and latency results above."
echo "  Expected performance characteristics:"
echo "    - MassTransit: Higher throughput (~12,500 msg/s) due to batch processing"
echo "    - Orleans Stream: Lower throughput (~13.5 msg/s) due to per-message acknowledgment"
echo ""


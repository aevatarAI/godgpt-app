#!/bin/bash
# Run all test scripts and generate a summary report

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPORT_FILE="$SCRIPT_DIR/test-results-$(date +%Y%m%d-%H%M%S).txt"

# Change to script directory so test-*.sh glob works correctly
cd "$SCRIPT_DIR"

echo "Running all test scripts..." | tee "$REPORT_FILE"
echo "Started at: $(date)" | tee -a "$REPORT_FILE"
echo "" | tee -a "$REPORT_FILE"

PASSED=0
FAILED=0
SKIPPED=0

for script in test-*.sh; do
    if [[ "$script" == "test-common.sh" ]] || [[ "$script" == "test-all-summary.sh" ]] || [[ "$script" == "test-summary.sh" ]]; then
        continue
    fi
    
    echo "========================================" | tee -a "$REPORT_FILE"
    echo "Testing: $script" | tee -a "$REPORT_FILE"
    echo "========================================" | tee -a "$REPORT_FILE"
    
    if [ ! -x "$script" ]; then
        echo "SKIPPED: $script (not executable)" | tee -a "$REPORT_FILE"
        ((SKIPPED++))
        continue
    fi
    
    # Use gtimeout if available (brew install coreutils), otherwise run without timeout
    if command -v gtimeout &> /dev/null; then
        TIMEOUT_CMD="gtimeout 120"
    elif command -v timeout &> /dev/null; then
        TIMEOUT_CMD="timeout 120"
    else
        TIMEOUT_CMD=""
    fi
    
    if $TIMEOUT_CMD bash "$script" >> "$REPORT_FILE" 2>&1; then
        echo "PASSED: $script" | tee -a "$REPORT_FILE"
        ((PASSED++))
    else
        echo "FAILED: $script" | tee -a "$REPORT_FILE"
        ((FAILED++))
    fi
    
    echo "" | tee -a "$REPORT_FILE"
done

echo "" | tee -a "$REPORT_FILE"
echo "========================================" | tee -a "$REPORT_FILE"
echo "Summary" | tee -a "$REPORT_FILE"
echo "========================================" | tee -a "$REPORT_FILE"
echo "Passed: $PASSED" | tee -a "$REPORT_FILE"
echo "Failed: $FAILED" | tee -a "$REPORT_FILE"
echo "Skipped: $SKIPPED" | tee -a "$REPORT_FILE"
echo "Completed at: $(date)" | tee -a "$REPORT_FILE"

echo ""
echo "Full report saved to: $REPORT_FILE"

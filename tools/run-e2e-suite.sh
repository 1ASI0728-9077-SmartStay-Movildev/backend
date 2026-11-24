#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

GATEWAY_URL="${SMARTSTAY_GATEWAY_URL:-http://localhost:8080}"
ACTIVEMQ_HOST="${ACTIVEMQ_HOST:-localhost}"
ACTIVEMQ_PORT="${ACTIVEMQ_PORT:-61616}"

TIER="all"
FEATURE=""
CLEAN=false
SKIP_PREFLIGHT=false
EXTRA_ARGS=()

show_usage() {
    cat << 'EOF'
================================================================================
 SmartStay Opaque-Box E2E Test Suite Runner
================================================================================
Usage:
  ./tools/run-e2e-suite.sh [options] [-- <dotnet-test-arguments>]

Options:
  --tier <1|2|3|4|all>     Filter by test tier (default: all)
                             1 = Tier 1: Isolated Feature Coverage
                             2 = Tier 2: Boundary & Corner Cases
                             3 = Tier 3: Pairwise Cross-Feature Interactions
                             4 = Tier 4: Real-World Stress & Recovery Scenarios
                             all = Run all tiers
  --feature <name|Fxx>     Filter by feature trait (e.g. F01, F08, Infrastructure)
  --clean                  Re-seed database before executing tests (via tools/seed-mvp.sh)
  --skip-preflight         Bypass Gateway and ActiveMQ connectivity checks
  --help                   Display this help message and exit
  -- [args...]             Forward trailing arguments directly to dotnet test
                           (e.g. -- --verbosity minimal, -- --filter FullyQualifiedName~...)

Examples:
  ./tools/run-e2e-suite.sh --tier 1
  ./tools/run-e2e-suite.sh --tier 1 --feature Infrastructure
  ./tools/run-e2e-suite.sh --skip-preflight --tier 1 -- --verbosity minimal
  ./tools/run-e2e-suite.sh --clean --tier 4
  ./tools/run-e2e-suite.sh --help
================================================================================
EOF
    exit 0
}

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case "$1" in
        --tier)
            if [[ -z "${2:-}" ]]; then
                echo "Error: --tier requires an argument (1, 2, 3, 4, or all)." >&2
                exit 1
            fi
            TIER="$2"
            shift 2
            ;;
        --feature)
            if [[ -z "${2:-}" ]]; then
                echo "Error: --feature requires an argument (e.g. F01, F08, Infrastructure)." >&2
                exit 1
            fi
            FEATURE="$2"
            shift 2
            ;;
        --clean)
            CLEAN=true
            shift
            ;;
        --skip-preflight)
            SKIP_PREFLIGHT=true
            shift
            ;;
        --help|-h)
            show_usage
            ;;
        1|2|3|4|all)
            TIER="$1"
            shift
            ;;
        --)
            shift
            EXTRA_ARGS=("$@")
            break
            ;;
        *)
            echo "Error: Unrecognized option '$1'. Use --help for usage information, or use '--' to pass extra arguments to dotnet test." >&2
            exit 1
            ;;
    esac
done

# Build dotnet test filter expression
FILTER_PARTS=()
if [ "$TIER" != "all" ]; then
    case "$TIER" in
        1|Tier1) FILTER_PARTS+=("Tier=Tier1") ;;
        2|Tier2) FILTER_PARTS+=("Tier=Tier2") ;;
        3|Tier3) FILTER_PARTS+=("Tier=Tier3") ;;
        4|Tier4) FILTER_PARTS+=("Tier=Tier4") ;;
        *)
            echo "Error: Invalid tier '$TIER'. Allowed values: 1, 2, 3, 4, all." >&2
            exit 1
            ;;
    esac
fi

if [ -n "$FEATURE" ]; then
    FILTER_PARTS+=("Feature=${FEATURE}")
fi

DOTNET_FILTER=""
if [ ${#FILTER_PARTS[@]} -gt 0 ]; then
    DOTNET_FILTER=$(IFS='&'; echo "${FILTER_PARTS[*]}")
fi

echo "================================================================================"
echo " SmartStay Opaque-Box E2E Test Runner                                          "
echo "================================================================================"
echo " Configuration:"
echo "   Gateway URL:       $GATEWAY_URL"
echo "   ActiveMQ Broker:   $ACTIVEMQ_HOST:$ACTIVEMQ_PORT"
echo "   Tier Selection:    $TIER"
echo "   Feature Filter:    ${FEATURE:-<None>}"
echo "   Effective Filter:  ${DOTNET_FILTER:-<All Tests>}"
echo "   Extra Arguments:   ${EXTRA_ARGS[*]:-<None>}"
echo "   Clean Re-seed:     $CLEAN"
echo "   Skip Pre-flight:   $SKIP_PREFLIGHT"
echo "================================================================================"

# Execute database clean/re-seed if requested
if [ "$CLEAN" = true ]; then
    echo "==> [Clean Step] Re-seeding database via tools/seed-mvp.sh..."
    if [ -x "$SCRIPT_DIR/seed-mvp.sh" ]; then
        "$SCRIPT_DIR/seed-mvp.sh"
    elif [ -f "$SCRIPT_DIR/seed-mvp.sh" ]; then
        bash "$SCRIPT_DIR/seed-mvp.sh"
    else
        docker compose -f "$ROOT_DIR/compose.microservices.yml" run --rm migrate seed
    fi
    echo "==> Database re-seeding complete."
fi

# Pre-flight health checks
if [ "$SKIP_PREFLIGHT" = false ]; then
    echo -n "==> [Pre-flight 1/2] Verifying Gateway Health ($GATEWAY_URL/health)... "
    HEALTH_CODE=$(curl -s -o /dev/null -w "%{http_code}" "$GATEWAY_URL/health" 2>/dev/null || true)
    if [ "$HEALTH_CODE" != "200" ]; then
        echo "FAILED (HTTP ${HEALTH_CODE:-offline})"
        echo ""
        echo "Error: API Gateway is not responding with HTTP 200 at $GATEWAY_URL/health."
        echo "Ensure the microservices stack is up via: docker compose -f compose.microservices.yml up -d"
        echo "Or pass --skip-preflight to run offline/contract unit checks."
        exit 1
    fi
    echo "OK (HTTP 200)"

    echo -n "==> [Pre-flight 2/2] Verifying ActiveMQ OpenWire ($ACTIVEMQ_HOST:$ACTIVEMQ_PORT)... "
    AMQ_ONLINE=false
    if nc -z -w3 "$ACTIVEMQ_HOST" "$ACTIVEMQ_PORT" 2>/dev/null; then
        AMQ_ONLINE=true
    elif timeout 3 bash -c "</dev/tcp/$ACTIVEMQ_HOST/$ACTIVEMQ_PORT" 2>/dev/null; then
        AMQ_ONLINE=true
    fi

    if [ "$AMQ_ONLINE" = false ]; then
        echo "FAILED"
        echo ""
        echo "Error: Cannot establish TCP connection to ActiveMQ on $ACTIVEMQ_HOST:$ACTIVEMQ_PORT."
        echo "Ensure ActiveMQ container is healthy: docker compose -f compose.microservices.yml ps"
        echo "Or pass --skip-preflight to run offline/contract unit checks."
        exit 1
    fi
    echo "OK"

    # Purge test runner and compensation queues via Jolokia
    purge_activemq_queues() {
        local AMQ_BASE="${SMARTSTAY_ACTIVEMQ_CONSOLE:-http://localhost:8161}"
        local QUEUES=(
            "Consumer.TestRunner.VirtualTopic.SmartStay.Payments.V1"
            "Consumer.TestRunner.VirtualTopic.SmartStay.Bookings.V1"
            "Consumer.Payments.VirtualTopic.SmartStay.Bookings.V1"
        )
        for q in "${QUEUES[@]}"; do
            curl -u admin:admin -H "Origin: $AMQ_BASE" -s \
                "$AMQ_BASE/api/jolokia/exec/org.apache.activemq:type=Broker,brokerName=localhost,destinationType=Queue,destinationName=$q/purge" \
                >/dev/null 2>&1 || true
        done
    }
    purge_activemq_queues
else
    echo "==> Pre-flight checks bypassed (--skip-preflight)."
fi

# Run dotnet test
cd "$ROOT_DIR"
TEST_PROJECT="tests/SmartStay.E2E.Tests/SmartStay.E2E.Tests.csproj"

DOTNET_CMD=(dotnet test "$TEST_PROJECT" --logger "console;verbosity=normal")

HAS_EXTRA_FILTER=false
for arg in "${EXTRA_ARGS[@]+"${EXTRA_ARGS[@]}"}"; do
    if [ "$arg" = "--filter" ]; then
        HAS_EXTRA_FILTER=true
        break
    fi
done

if [ -n "$DOTNET_FILTER" ] && [ "$HAS_EXTRA_FILTER" = false ]; then
    DOTNET_CMD+=(--filter "$DOTNET_FILTER")
fi
if [ ${#EXTRA_ARGS[@]} -gt 0 ]; then
    DOTNET_CMD+=("${EXTRA_ARGS[@]}")
fi

echo ""
echo "==> Executing E2E Test Suite..."
echo "    Command: ${DOTNET_CMD[*]}"
echo "================================================================================"

START_TIME=$(date +%s)
set +e
"${DOTNET_CMD[@]}"
EXIT_CODE=$?
set -e
END_TIME=$(date +%s)
DURATION=$((END_TIME - START_TIME))

echo "================================================================================"
if [ "$EXIT_CODE" -eq 0 ]; then
    echo " [SUCCESS] E2E test execution completed successfully in ${DURATION}s."
else
    echo " [FAILED]  E2E test execution failed with exit code $EXIT_CODE in ${DURATION}s."
fi
echo "================================================================================"

exit "$EXIT_CODE"

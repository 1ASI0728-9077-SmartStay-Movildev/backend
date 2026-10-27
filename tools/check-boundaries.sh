#!/usr/bin/env bash
set -euo pipefail

echo "=========================================================="
echo " SmartStay Boundary Checker: Validating Service Isolation "
echo "=========================================================="

FAILED=0

# Revisar cada proyecto de servicio en src/Services/
for csproj in $(find src/Services -name "*.csproj"); do
    SERVICE_NAME=$(basename "$csproj" .csproj)
    echo "Inspeccionando dependencias de: $SERVICE_NAME"

    # 1. Prohibir referencias a BackendAwSmartstay.API (el monolito)
    if grep -q "BackendAwSmartstay.API.csproj" "$csproj"; then
        echo " [ERROR] $SERVICE_NAME contiene referencia prohibida al monolito BackendAwSmartstay.API!"
        FAILED=1
    fi

    # 2. Prohibir referencias a otros microservicios
    for other_service in $(find src/Services -name "*.csproj" ! -path "$csproj"); do
        OTHER_NAME=$(basename "$other_service" .csproj)
        if grep -q "$OTHER_NAME.csproj" "$csproj"; then
            echo " [ERROR] $SERVICE_NAME contiene referencia prohibida al microservicio $OTHER_NAME!"
            FAILED=1
        fi
    done
done

if [ "$FAILED" -eq 0 ]; then
    echo " [SUCCESS] Todas las fronteras arquitectónicas respetan el aislamiento estricto (0 referencias cruzadas)."
    exit 0
else
    echo " [FAILED] Se encontraron violaciones a los límites de microservicios."
    exit 1
fi

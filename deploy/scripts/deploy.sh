#!/usr/bin/env bash

set -Eeuo pipefail

DEPLOYMENT_ROOT="/opt/ironforge"
COMPOSE_FILE="${DEPLOYMENT_ROOT}/compose/compose.production.yml"
ENV_FILE="${DEPLOYMENT_ROOT}/env/production.env"
STATE_DIR="${DEPLOYMENT_ROOT}/state"
CURRENT_RELEASE_FILE="${STATE_DIR}/current-release"
PREVIOUS_RELEASE_FILE="${STATE_DIR}/previous-release"

IMAGE_TAG="${1:-}"

if [[ -z "$IMAGE_TAG" ]]; then
    echo "ERROR: image tag is required."
    echo "Usage: $0 <image-tag>"
    exit 1
fi

if [[ ! "$IMAGE_TAG" =~ ^[a-f0-9]{40}$ ]]; then
    echo "ERROR: image tag must be a 40-character Git commit SHA."
    exit 1
fi

mkdir -p "${STATE_DIR}"
chmod 700 "${STATE_DIR}"

CURRENT_RELEASE=""

if [[ -s "${CURRENT_RELEASE_FILE}" ]]; then
    CURRENT_RELEASE=$(cat "${CURRENT_RELEASE_FILE}")
else
    echo "ERROR: No current release recorded."
    echo "Initialize current-release from the verified running deployment."
    exit 1
fi

if [[ ! "${CURRENT_RELEASE}" =~ ^[a-f0-9]{40}$ ]]; then
    echo "ERROR: current-release contains an invalid commit SHA."
    exit 1
fi

echo "Current successful release: ${CURRENT_RELEASE}"
echo "Requested release:          ${IMAGE_TAG}"




DEPLOYMENT_STARTED=0
ROLLBACK_IN_PROGRESS=0

rollback_on_failure() {
    local original_status="$1"

    # No rollback for failures before deployment changes begin.
    if [[ "$original_status" -eq 0 ||
          "$DEPLOYMENT_STARTED" -ne 1 ||
          "$ROLLBACK_IN_PROGRESS" -eq 1 ]]; then
        return
    fi

    ROLLBACK_IN_PROGRESS=1
    trap - EXIT
    set +e

    echo "ERROR: Deployment of ${IMAGE_TAG} failed."
    echo "Attempting rollback to ${CURRENT_RELEASE}..."

    # Restore the previously successful image tag.
    if grep -q '^IRONFORGE_IMAGE_TAG=' "${ENV_FILE}"; then
        sed -i \
            "s/^IRONFORGE_IMAGE_TAG=.*/IRONFORGE_IMAGE_TAG=${CURRENT_RELEASE}/" \
            "${ENV_FILE}"
    else
        printf '\nIRONFORGE_IMAGE_TAG=%s\n' "${CURRENT_RELEASE}" \
            >> "${ENV_FILE}"
    fi

    # Reconcile the application with the previous image version.
    if docker compose \
        --env-file "${ENV_FILE}" \
        -f "${COMPOSE_FILE}" \
        pull &&
       docker compose \
        --env-file "${ENV_FILE}" \
        -f "${COMPOSE_FILE}" \
        up -d; then

        echo "Previous release containers have been restored."

        # Verify API and MCP health after rollback.
        local service
        local container_id
        local status
        local attempt
        local rollback_ok=1

        for service in ironforge-api ironforge-mcp; do
            container_id=$(
                docker compose \
                    --env-file "${ENV_FILE}" \
                    -f "${COMPOSE_FILE}" \
                    ps -q "$service"
            )

            if [[ -z "$container_id" ]]; then
                echo "Rollback verification failed: $service has no container."
                rollback_ok=0
                continue
            fi

            for ((attempt = 1; attempt <= 30; attempt++)); do
                status=$(docker inspect \
                    --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' \
                    "$container_id" 2>/dev/null)

                if [[ "$status" == "healthy" ]]; then
                    break
                fi

                if [[ "$status" == "unhealthy" ||
                      "$status" == "exited" ||
                      "$status" == "dead" ||
                      "$status" == "" ]]; then
                    break
                fi

                sleep 5
            done

            if [[ "$status" != "healthy" ]]; then
                echo "Rollback verification failed: $service status=$status"
                rollback_ok=0
            fi
        done

        if ! curl --fail --silent --show-error \
            --max-time 5 http://127.0.0.1/ -o /dev/null; then
            echo "Rollback verification failed: Web endpoint is unavailable."
            rollback_ok=0
        fi

        if [[ "$rollback_ok" -eq 1 ]]; then
            printf '%s\n' "${CURRENT_RELEASE}" \
                > "${DEPLOYMENT_ROOT}/state/current-release"
            echo "Rollback completed and health checks passed."
        else
            echo "CRITICAL: Rollback started, but health verification failed."
        fi
    else
        echo "CRITICAL: Could not restore the previous release."
    fi

    # Preserve the original deployment failure for GitHub Actions.
    exit "$original_status"
}

trap 'rollback_on_failure $?' EXIT



if [[ "${CURRENT_RELEASE}" != "${IMAGE_TAG}" ]]; then
    printf '%s\n' "${CURRENT_RELEASE}" \
        > "${PREVIOUS_RELEASE_FILE}.tmp"

    chmod 600 "${PREVIOUS_RELEASE_FILE}.tmp"
    mv "${PREVIOUS_RELEASE_FILE}.tmp" "${PREVIOUS_RELEASE_FILE}"

    echo "Preserved previous release: ${CURRENT_RELEASE}"
else
    echo "Requested release is already the current successful release."
fi

DEPLOYMENT_STARTED=1

echo "Deploying IronForge image tag: ${IMAGE_TAG}"

cd "${DEPLOYMENT_ROOT}/compose"

echo "Updating production image tag..."

if grep -q '^IRONFORGE_IMAGE_TAG=' "${ENV_FILE}"; then
    sed -i "s/^IRONFORGE_IMAGE_TAG=.*/IRONFORGE_IMAGE_TAG=${IMAGE_TAG}/" "${ENV_FILE}"
else
    printf '\nIRONFORGE_IMAGE_TAG=%s\n' "${IMAGE_TAG}" >> "${ENV_FILE}"
fi

echo "Validating Compose configuration..."

docker compose \
    --env-file "${ENV_FILE}" \
    -f "${COMPOSE_FILE}" \
    config >/dev/null

echo "Pulling production images..."

docker compose \
    --env-file "${ENV_FILE}" \
    -f "${COMPOSE_FILE}" \
    pull

echo "Running database migrations..."

docker compose \
    --env-file "${ENV_FILE}" \
    -f "${COMPOSE_FILE}" \
    run --rm \
    ironforge-migrator


echo "Starting IronForge..."

docker compose \
    --env-file "${ENV_FILE}" \
    -f "${COMPOSE_FILE}" \
    up -d

echo "Waiting for API and MCP health checks..."

wait_for_health() {
    local service="$1"
    local attempts=30
    local delay=5
    local container_id
    local status

    container_id=$(
        docker compose \
            --env-file "${ENV_FILE}" \
            -f "${COMPOSE_FILE}" \
            ps -q "$service"
    )

    if [[ -z "$container_id" ]]; then
        echo "ERROR: No container found for service: $service"
        return 1
    fi

    for ((i = 1; i <= attempts; i++)); do
        status=$(docker inspect \
            --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' \
            "$container_id")

        echo "$service status: $status ($i/$attempts)"

        if [[ "$status" == "healthy" ]]; then
            return 0
        fi

        if [[ "$status" == "unhealthy" || "$status" == "exited" || "$status" == "dead" ]]; then
            docker compose \
                --env-file "${ENV_FILE}" \
                -f "${COMPOSE_FILE}" \
                logs --tail=80 "$service" || true
            return 1
        fi

        sleep "$delay"
    done

    echo "ERROR: Timed out waiting for $service."
    return 1
}

wait_for_health ironforge-api
wait_for_health ironforge-mcp

echo "Checking public Web endpoint through Caddy..."

for ((i = 1; i <= 12; i++)); do
    if curl --fail --silent --show-error \
        --max-time 5 \
        http://127.0.0.1/ \
        -o /dev/null; then
        echo "Public Web endpoint is responding."
        break
    fi

    if [[ "$i" -eq 12 ]]; then
        echo "ERROR: Public Web endpoint failed verification."
        docker compose \
            --env-file "${ENV_FILE}" \
            -f "${COMPOSE_FILE}" \
            logs --tail=80 caddy ironforge-web || true
        exit 1
    fi

    sleep 5
done


docker compose \
    --env-file "${ENV_FILE}" \
    -f "${COMPOSE_FILE}" \
    ps

printf '%s\n' "${IMAGE_TAG}" \
    > "${CURRENT_RELEASE_FILE}.tmp"

chmod 600 "${CURRENT_RELEASE_FILE}.tmp"
mv "${CURRENT_RELEASE_FILE}.tmp" "${CURRENT_RELEASE_FILE}"

echo "Deployment and health verification completed successfully."


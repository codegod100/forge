# Run the app in watch mode
watch:
    password_file="${FORGE_ADMIN_PASSWORD_FILE:-${XDG_RUNTIME_DIR:-/run/user/$(id -u)}/agenix/forge-admin-password}"; \
    if [ ! -f "$password_file" ]; then \
        echo "Forge admin password file not found: $password_file"; \
        exit 1; \
    fi; \
    export Auth__PasswordFile="$password_file"; \
    echo "Forge starting at: http://localhost:5128"; \
    dotnet watch --project src/Forge.Web

# Run the app once
run:
    nix run .#run-forge

# Build the project
build:
    dotnet build

# Run tests
test:
    dotnet test

# Deploy to Fly.io
deploy:
    nix run .#deploy-fly

# Set Fly.io secrets from local agenix
fly-secrets:
    #!/usr/bin/env bash
    source .env
    password_file="${FORGE_ADMIN_PASSWORD_FILE:-${XDG_RUNTIME_DIR:-/run/user/$(id -u)}/agenix/forge-admin-password}"
    password=$(tr -d '\n' < "$password_file")
    fly secrets set -a "$FLY_APP" \
        Auth__Password="$password" \
        Auth__Username=admin \
        Database__Path=/data/forge.db \
        Repositories__Root=/data/repositories \
        ASPNETCORE_ENVIRONMENT=Production

# Create Fly.io volume
fly-volume:
    fly volumes create forge_data --region sjc --size 1 -a $(grep FLY_APP .env | cut -d= -f2)

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

# Create Fly.io volume
fly-volume:
    fly volumes create forge_data --region sjc --size 1 -a $(grep FLY_APP .env | cut -d= -f2)

# Forge

Forge is a small self-hosted Git forge built with ASP.NET Core, Blazor, SQLite, and Git Smart HTTP.

## Features

- Browse repositories, trees, blobs, commits, and branches
- Create repositories from the web UI
- Push over HTTP to auto-create a missing repository on first push
- Cookie-based web login with Basic auth support for Git operations
- Repository visibility controls with private/public settings
- README rendering on the repository page
- Syntax highlighting for source files and fenced code blocks

## Running Locally

```bash
nix run .#run-forge
```

Open `http://localhost:5128`.

The admin username is `admin`.

For local runtime, the app now expects a decrypted password file path, which matches the Home Manager agenix pattern. By default the flake looks for:

`$XDG_RUNTIME_DIR/agenix/forge-admin-password`

Override it with:

```bash
export FORGE_ADMIN_PASSWORD_FILE=/path/to/decrypted/password-file
```

## Railway Deployment

This repo includes:

- [Dockerfile](/home/nandi/code/forge/Dockerfile) for Railway builds
- [flake.nix](/home/nandi/code/forge/flake.nix) apps `run-forge` and `deploy-railway`
- agenix config in [secrets.nix](/home/nandi/code/forge/secrets.nix)

Deploy flow:

```bash
export RAILWAY_PROJECT=<project-id>
export RAILWAY_ENVIRONMENT=<environment-id-or-name>
export RAILWAY_SERVICE=<service-name>
nix run .#deploy-railway
```

The deploy script:

- reads the admin password from `FORGE_ADMIN_PASSWORD_FILE`
- sets `Auth__Username`, `Auth__Password`, `Database__Path`, and `Repositories__Root`
- runs `railway up --detach`

Railway volume note:

- Attach a Railway volume mounted at `/data`
- The deployment expects to store `forge.db` and bare Git repositories under `/data`

## Push This Repo To Forge

```bash
git remote add forge "http://admin:$(cat ${FORGE_ADMIN_PASSWORD_FILE:-${XDG_RUNTIME_DIR}/agenix/forge-admin-password})@localhost:5128/admin/forge.git"
git push -u forge main
```

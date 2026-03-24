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
dotnet run --project src/Forge.Web
```

Open `http://localhost:5128`.

## Development Credentials

Development config currently uses:

- Username: `admin`
- Password: `forge-dev-password`

## Push This Repo To Forge

```bash
git remote add forge http://admin:forge-dev-password@localhost:5128/admin/forge.git
git push -u forge main
```

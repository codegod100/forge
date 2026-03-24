{
  description = "Forge - A Blazor Git Forge";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
    flake-utils.url = "github:numtide/flake-utils";
  };

  outputs = { self, nixpkgs, flake-utils }:
    flake-utils.lib.eachDefaultSystem (system:
      let
        pkgs = nixpkgs.legacyPackages.${system};
        dotnet-sdk = pkgs.dotnetCorePackages.dotnet_9.sdk;
        railwayCli = pkgs.railway;

        runForge = pkgs.writeShellApplication {
          name = "run-forge";
          runtimeInputs = [ dotnet-sdk ];
          text = ''
            password_file="''${FORGE_ADMIN_PASSWORD_FILE:-''${XDG_RUNTIME_DIR:-/run/user/$(id -u)}/agenix/forge-admin-password}"
            if [ ! -f "$password_file" ]; then
              echo "Forge admin password file not found: $password_file"
              exit 1
            fi
            export Auth__PasswordFile="$password_file"
            exec dotnet run --project src/Forge.Web "$@"
          '';
        };

        deployRailway = pkgs.writeShellApplication {
          name = "deploy-railway";
          runtimeInputs = [ railwayCli ];
          text = ''
            password_file="''${FORGE_ADMIN_PASSWORD_FILE:-''${XDG_RUNTIME_DIR:-/run/user/$(id -u)}/agenix/forge-admin-password}"
            if [ ! -f "$password_file" ]; then
              echo "Forge admin password file not found: $password_file"
              exit 1
            fi

            password="$(tr -d '\n' < "$password_file")"
            if [ -z "$password" ]; then
              echo "Forge admin password file is empty: $password_file"
              exit 1
            fi

            variableArgs=()
            upArgs=()
            if [ -n "''${RAILWAY_PROJECT:-}" ]; then
              linkArgs=(--project "$RAILWAY_PROJECT")
              if [ -n "''${RAILWAY_ENVIRONMENT:-}" ]; then
                linkArgs+=(--environment "$RAILWAY_ENVIRONMENT")
              fi
              railway link "''${linkArgs[@]}"
              upArgs+=(--project "$RAILWAY_PROJECT")
            fi
            if [ -n "''${RAILWAY_ENVIRONMENT:-}" ]; then
              variableArgs+=(-e "$RAILWAY_ENVIRONMENT")
              upArgs+=(-e "$RAILWAY_ENVIRONMENT")
            fi
            if [ -n "''${RAILWAY_SERVICE:-}" ]; then
              variableArgs+=(-s "$RAILWAY_SERVICE")
              upArgs+=(-s "$RAILWAY_SERVICE")
            fi

            railway variable set "''${variableArgs[@]}" --skip-deploys Auth__Username=admin
            railway variable set "''${variableArgs[@]}" --skip-deploys Database__Path=/data/forge.db
            railway variable set "''${variableArgs[@]}" --skip-deploys Repositories__Root=/data/repositories
            railway variable set "''${variableArgs[@]}" --skip-deploys ASPNETCORE_ENVIRONMENT=Production

            printf '%s' "$password" | railway variable set "''${variableArgs[@]}" --skip-deploys --stdin Auth__Password

            railway up "''${upArgs[@]}" --detach
          '';
        };
      in
      {
        packages.default = pkgs.buildDotnetModule {
          pname = "forge";
          version = "0.1.0";
          src = ./.;

          projectFile = "Forge.sln";
          nugetDeps = ./deps.nix;

          dotnet-sdk = dotnet-sdk;
          dotnet-runtime = pkgs.dotnetCorePackages.dotnet_9.runtime;

          executables = [ "Forge.Web" ];
        };

        packages.run-forge = runForge;
        packages.deploy-railway = deployRailway;

        apps.run-forge = {
          type = "app";
          program = "${runForge}/bin/run-forge";
        };

        apps.deploy-railway = {
          type = "app";
          program = "${deployRailway}/bin/deploy-railway";
        };

        devShells.default = pkgs.mkShell {
          packages = with pkgs; [
            dotnet-sdk
            git
            railwayCli
            sqlite
          ];

          shellHook = ''
            echo "Forge dev shell"
            echo "Commands:"
            echo "  nix run .#run-forge          Start the dev server using a decrypted password file"
            echo "  dotnet test                Run tests"
            echo "  dotnet build               Build the project"
            echo "  nix run .#deploy-railway   Deploy to Railway using a decrypted password file"
            echo ""
            echo "Git clone/push URLs:"
            echo "  git clone http://localhost:5128/{owner}/{repo}.git"
            echo "  git push http://localhost:5128/{owner}/{repo}.git main"
          '';
        };

      }
    );
}

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
        flyctl = pkgs.flyctl;

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
            echo "Forge starting at: http://localhost:5128"
            exec dotnet run --project src/Forge.Web "$@"
          '';
        };

        deployFly = pkgs.writeShellApplication {
          name = "deploy-fly";
          runtimeInputs = [ flyctl ];
          text = ''
            # shellcheck source=/dev/null
            # Load local .env if exists
            if [ -f .env ]; then
              set -a
              source .env
              set +a
            fi

            if [ -z "''${FLY_APP:-}" ]; then
              echo "Error: FLY_APP not set. Create a .env file with FLY_APP=<your-app-name>"
              exit 1
            fi

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

            # Stage secrets without deploying
            fly secrets set -a "$FLY_APP" --stage Auth__Password="$password"
            fly secrets set -a "$FLY_APP" --stage Auth__Username=admin
            fly secrets set -a "$FLY_APP" --stage Database__Path=/data/forge.db
            fly secrets set -a "$FLY_APP" --stage Repositories__Root=/data/repositories
            fly secrets set -a "$FLY_APP" --stage ASPNETCORE_ENVIRONMENT=Production

            # Deploy with staged secrets
            fly deploy -a "$FLY_APP"
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
        packages.deploy-fly = deployFly;

        apps.run-forge = {
          type = "app";
          program = "${runForge}/bin/run-forge";
        };

        apps.deploy-fly = {
          type = "app";
          program = "${deployFly}/bin/deploy-fly";
        };

        devShells.default = pkgs.mkShell {
          packages = with pkgs; [
            dotnet-sdk
            git
            flyctl
            sqlite
            just
          ];

          shellHook = ''
            echo "Forge dev shell"
            echo "Commands:"
            echo "  nix run .#run-forge        Start the dev server using a decrypted password file"
            echo "  dotnet test                Run tests"
            echo "  dotnet build               Build the project"
            echo "  nix run .#deploy-fly       Deploy to Fly.io using a decrypted password file"
            echo ""
            echo "Git clone/push URLs:"
            echo "  git clone http://localhost:5128/{owner}/{repo}.git"
            echo "  git push http://localhost:5128/{owner}/{repo}.git main"
          '';
        };

      }
    );
}

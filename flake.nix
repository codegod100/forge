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
        dotnet-runtime = pkgs.dotnetCorePackages.dotnet_9.runtime;
        flyctl = pkgs.flyctl;

        forge = pkgs.buildDotnetModule rec {
          name = "forge";
          src = ./.;
          projectFile = "src/Forge.Web/Forge.Web.csproj";
          nugetDeps = ./deps.json;
          dotnet-sdk = pkgs.dotnetCorePackages.dotnet_9.sdk;
          dotnet-runtime = pkgs.dotnetCorePackages.dotnet_9.runtime;
          executables = [ "Forge.Web" ];
          
          # Only build for linux-x64
          runtimeId = "linux-x64";
          buildType = "Release";
          selfContainedBuild = false;
        };

        runForge = pkgs.writeShellApplication {
          name = "run-forge";
          runtimeInputs = [ dotnet-runtime ];
          text = ''
            password_file="''${FORGE_ADMIN_PASSWORD_FILE:-''${XDG_RUNTIME_DIR:-/run/user/$(id -u)}/agenix/forge-admin-password}"
            if [ ! -f "$password_file" ]; then
              echo "Forge admin password file not found: $password_file"
              exit 1
            fi
            export Auth__PasswordFile="$password_file"
            echo "Forge starting at: http://localhost:5128"
            exec ${forge}/bin/Forge.Web --urls "http://localhost:5128" "$@"
          '';
        };

        deployFly = pkgs.writeShellApplication {
          name = "deploy-fly";
          runtimeInputs = [ flyctl ];
          text = ''
            # shellcheck source=/dev/null
            if [ -f .env ]; then
              set -a
              source .env
              set +a
            fi

            if [ -z "''${FLY_APP:-}" ]; then
              echo "Error: FLY_APP not set. Create a .env file with FLY_APP=<your-app-name>"
              exit 1
            fi

            fly deploy -a "$FLY_APP"
          '';
        };
      in
      {
        packages.default = runForge;
        packages.forge = forge;
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
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

        devShells.default = pkgs.mkShell {
          packages = with pkgs; [
            dotnet-sdk
            git
            sqlite
          ];

          shellHook = ''
            echo "Forge dev shell"
            echo "Commands:"
            echo "  dotnet run --project src/Forge.Web  Start the dev server"
            echo "  dotnet test                Run tests"
            echo "  dotnet build               Build the project"
            echo ""
            echo "Git clone/push URLs:"
            echo "  git clone http://localhost:5128/{owner}/{repo}.git"
            echo "  git push http://localhost:5128/{owner}/{repo}.git main"
          '';
        };

      }
    );
}

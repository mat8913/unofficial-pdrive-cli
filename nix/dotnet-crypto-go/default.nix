{ lib, buildGoModule, callPackage }:


(buildGoModule (finalAttrs: {
  pname = "dotnet-crypto-go";
  version = "0.10.4";

  src = (callPackage ../sources.nix { }).dotnet-crypto;

  sourceRoot = "${finalAttrs.src.name}/src/go";

  GOFLAGS = [ "-buildmode=c-shared" ];

  vendorHash = "sha256-BDiwXkuM5NobdfmsS4fGpprCEvNxH+qQ/SE2/4hiB08=";
}))
# Workaround for https://github.com/NixOS/nixpkgs/issues/379710
.overrideAttrs (
  finalAttrs: previousAttrs: {
    buildPhase = lib.replaceString "buildGoDir install" "buildGoDir build" previousAttrs.buildPhase;
    installPhase = ''
        mkdir -p "$out"/lib
        cp extern "$out"/lib/libproton_crypto.so
    '';
  }
)

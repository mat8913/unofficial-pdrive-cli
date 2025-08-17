{
  lib,
  stdenvNoCC,
  buildDotnetModule,
  callPackage,
  dotnetCorePackages,
  dotnet-crypto-go
}:

buildDotnetModule rec {
  pname = "dotnet-crypto-cs";
  version = "0.10.4";

  src = (callPackage ../sources.nix { }).dotnet-crypto;

  projectFile = "src/dotnet/Proton.Cryptography.csproj";
  nugetDeps = ./deps.json;

  dotnet-sdk = dotnetCorePackages.sdk_8_0;
  dotnet-runtime = dotnetCorePackages.runtime_8_0;

  dotnetFlags = "-p:Version=${version}";

  packNupkg = true;

  runtimeId = dotnetCorePackages.systemToDotnetRid stdenvNoCC.hostPlatform.system;

  preBuild = ''
    mkdir -p "bin/runtimes/${runtimeId}/native/"
    cp "${dotnet-crypto-go}/lib/libproton_crypto.so" "bin/runtimes/${runtimeId}/native/"
  '';

  # Workaround for https://github.com/NixOS/nixpkgs/issues/283430
  preInstall = ''
    cp "src/dotnet/bin/Release/net8.0/${runtimeId}/"{*.dll,*.pdb} src/dotnet/bin/Release/net8.0/
  '';
}

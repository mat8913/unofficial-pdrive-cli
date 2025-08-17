{
  stdenvNoCC,
  buildDotnetModule,
  callPackage,
  dotnetCorePackages,
  dotnet-crypto-cs
}:

let template = { suffix, csproject }:

  buildDotnetModule rec {
    pname = "sdk-tech-demo-${suffix}";
    version = "1.0.0";

    src = (callPackage ../sources.nix { }).sdk-tech-demo;

    projectFile = "src/${csproject}/${csproject}.csproj";
    nugetDeps = ./deps.json;

    dotnet-sdk = dotnetCorePackages.sdk_9_0;
    dotnet-runtime = dotnetCorePackages.runtime_9_0;

    buildInputs = [
      dotnet-crypto-cs
    ];

    dotnetFlags = "-p:Version=${version}";

    packNupkg = true;

    runtimeId = dotnetCorePackages.systemToDotnetRid stdenvNoCC.hostPlatform.system;

    # Workaround for https://github.com/NixOS/nixpkgs/issues/283430
    preInstall = ''
      cp "src/${csproject}/bin/Release/net9.0/${runtimeId}/"{*.dll,*.pdb} src/${csproject}/bin/Release/net9.0/
    '';
  };

in

{
  sdk = template {
    suffix = "sdk";
    csproject = "Proton.Sdk";
  };

  instrumentation = template {
    suffix = "instrumentation";
    csproject = "Proton.Sdk.Instrumentation";
  };

  drive = template {
    suffix = "drive";
    csproject = "Proton.Sdk.Drive";
  };
}

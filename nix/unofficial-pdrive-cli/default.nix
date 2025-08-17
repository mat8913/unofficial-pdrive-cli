{
  buildDotnetModule,
  dotnetCorePackages,
  dotnet-crypto-cs,
  sdk-tech-demo
}:

buildDotnetModule rec {
  pname = "unofficial-pdrive-cli";
  version = "0.1";

  src = ../..;

  projectFile = "unofficial-pdrive-cli/unofficial-pdrive-cli.csproj";
  nugetDeps = ./deps.json;

  dotnet-sdk = dotnetCorePackages.sdk_9_0;
  dotnet-runtime = dotnetCorePackages.runtime_9_0;

  buildInputs = [
    dotnet-crypto-cs
    sdk-tech-demo.sdk
    sdk-tech-demo.instrumentation
    sdk-tech-demo.drive
  ];
}

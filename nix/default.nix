{ callPackage }:

rec {
  dotnet-crypto-go = callPackage ./dotnet-crypto-go { };

  dotnet-crypto-cs = callPackage ./dotnet-crypto-cs { inherit dotnet-crypto-go; };

  sdk-tech-demo = callPackage ./sdk-tech-demo { inherit dotnet-crypto-cs; };

  unofficial-pdrive-cli = callPackage ./unofficial-pdrive-cli { inherit dotnet-crypto-cs sdk-tech-demo; };
}

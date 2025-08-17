# unofficial-pdrive-cli

An unofficial Proton Drive command-line client based off the SDK tech demo.

## Warnings

* The Proton devs strongly discourage the use of the SDK tech demo by 3rd party applications. I have used it anyway.
* I wrote this for myself and it has recieved very limited testing.
* **Keep backups. Data loss may occur due to unforeseen errors. You have been warned.**

## Build

If you use NixOS, you can just run `nix-build`. Otherwise, you will need to:

* Build https://github.com/ProtonDriveApps/dotnet-crypto and put the nuget package in your local nuget repo
* Build https://github.com/ProtonDriveApps/sdk-tech-demo and put the nuget packages in your local nuget repo
* Run `dotnet build` to build this

## Usage

```
Usage: unofficial-pdrive-cli [<flags>] <command> [<args>]

Commands:

    login                          -  logs in to Proton Drive
    get <remote src> <local dest>  -  downloads from Proton Drive
    put <local src> <remote dest>  -  uploads to Proton Drive

Flags:

    --enable-sdk-log  -  enables log output from the Proton SDK
    --overwrite       -  allow local and remote files to be overwritten
    --recursive       -  recurse into directories
```

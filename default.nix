{ nixpkgs ? import <nixpkgs> {} }:

(nixpkgs.callPackage ./nix {}).unofficial-pdrive-cli

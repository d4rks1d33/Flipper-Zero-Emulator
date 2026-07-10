#!/usr/bin/env bash

# Install Runner as a global dotnet tool

cd ModelsAnalyzer/Runner
dotnet pack --configuration Release
dotnet tool update --global --add-source ./package RenodeAnalyzersRunner

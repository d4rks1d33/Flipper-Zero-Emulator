#!/bin/bash

mkdir BigGustave
cd src/BigGustave/
msbuild -restore BigGustave.csproj
cd -
cp ./src/BigGustave/bin/Debug/net45/BigGustave.dll ./BigGustave/
cp ./LICENSE ./BigGustave/BigGustave-license


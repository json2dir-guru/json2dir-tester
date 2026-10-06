#!/bin/sh
# Builds and runs the tester on Linux.
# Usage: ./run.sh run json2dir     (arguments go to json2dir-tester as is)
set -e
dir=$(dirname "$0")
dotnet build "$dir/src/Json2dirTester" -c Release --nologo -v quiet >&2
exec dotnet "$dir/src/Json2dirTester/bin/Release/net10.0/json2dir-tester.dll" "$@"

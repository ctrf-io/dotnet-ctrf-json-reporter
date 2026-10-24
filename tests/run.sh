#!/usr/bin/env bash
# Native equivalents of test:standard:*; C# runs every selected scope after failures.
set -uo pipefail
root=$(cd "$(dirname "$0")/.." && pwd) || exit 1
selection=${1:-standard}
case "$selection" in ordinary|checker|unit|integration|conformance|standard|all) ;; *) echo "Unknown suite: $selection" >&2; exit 2;; esac
dotnet_command=${DOTNET_COMMAND:-dotnet}
if [[ -n "${CTRF_STANDARD_ARTIFACTS:-}" ]]; then
  for scope in reporter-tests checker unit integration; do
    case "$selection:$scope" in
      all:*|ordinary:reporter-tests|checker:checker|unit:unit|integration:integration|conformance:unit|conformance:integration|standard:checker|standard:unit|standard:integration)
        folder=reports
        if [[ "$scope" == checker ]]; then folder=checker-reports; fi
        if [[ "$scope" == reporter-tests ]]; then folder=reporter-reports; fi
        rm -f "$CTRF_STANDARD_ARTIFACTS/$folder/$scope.json" || exit 1 ;;
    esac
  done
fi
cd "$root" || exit 1
"$dotnet_command" build tests/ConformanceHarness.csproj -c Release --no-restore || exit 1
"$dotnet_command" tests/bin/Release/net8.0/ConformanceHarness.dll "$selection"

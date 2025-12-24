#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage:
  scripts/lint.sh [--fix]

Does:
  - dotnet build -c Release
  - dotnet format (if available)

Notes:
  - Formatting requires 'dotnet format' (dotnet-format). If it isn't installed, the script will still build
    and will print install instructions.
EOF
}

fix=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help) usage; exit 0 ;;
    --fix) fix=1; shift ;;
    *) echo "error: unknown arg: $1" >&2; usage >&2; exit 2 ;;
  esac
done

repoRoot="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repoRoot"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "error: dotnet not found. Install the .NET SDK (Windows recommended for this WinForms app)." >&2
  exit 1
fi

dotnet build TextFileWatch.csproj -c Release

if dotnet format --version >/dev/null 2>&1; then
  if [[ "$fix" == "1" ]]; then
    dotnet format TextFileWatch.csproj
  else
    dotnet format TextFileWatch.csproj --verify-no-changes
  fi
else
  echo "warning: 'dotnet format' not found; skipping formatting." >&2
  echo "Install it with: dotnet tool install -g dotnet-format" >&2
fi

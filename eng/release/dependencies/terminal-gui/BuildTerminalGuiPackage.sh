#!/usr/bin/env bash
set -euo pipefail

if [[ ${1:-} != "--write" && ${1:-} != "--check" ]]; then
  echo "Usage: BuildTerminalGuiPackage.sh (--write|--check)" >&2
  exit 64
fi

repo_root=$(git rev-parse --show-toplevel)
dependency_root="$repo_root/eng/release/dependencies/terminal-gui"
verifier="$dependency_root/VerifyTerminalGuiPackage.cs"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=1
export DOTNET_NOLOGO=1

temporary_root=${TMPDIR:-/tmp}
work_root=$(mktemp -d "$temporary_root/islamu-terminal-gui.XXXXXX")
cleanup() {
  if [[ $work_root == "$temporary_root"/islamu-terminal-gui.* ]]; then
    rm -rf -- "$work_root"
  fi
}
trap cleanup EXIT

source_root="$work_root/source"
package_cache="$work_root/packages"
package_output="$work_root/output"
feed="$dependency_root/feed"
package="$feed/ISLAMU.Terminal.Gui.2.4.17-islamu.1.nupkg"

git clone --branch v2.4.17 --depth 1 https://github.com/tui-cs/Terminal.Gui.git "$source_root"
[[ $(git -C "$source_root" rev-parse refs/tags/v2.4.17) == 58f3af1a4afe5d2772be134b2299a0f78f35c93c ]]
[[ $(git -C "$source_root" rev-parse 'refs/tags/v2.4.17^{}') == d0a0ed9b150d3fc8aacf4ab07b7f7d91264fe6d6 ]]
git -C "$source_root" apply --check "$dependency_root/patches/0001-remove-textmate-grammars.patch"
git -C "$source_root" apply "$dependency_root/patches/0001-remove-textmate-grammars.patch"

NUGET_PACKAGES="$package_cache" dotnet build "$source_root/Terminal.Gui/Terminal.Gui.csproj" \
  --configuration Release -p:IsISLAMUDownstreamBuild=true \
  -p:GeneratePackageOnBuild=false -p:ContinuousIntegrationBuild=true --verbosity minimal
NUGET_PACKAGES="$package_cache" dotnet pack "$source_root/Terminal.Gui/Terminal.Gui.csproj" \
  --configuration Release --no-build --output "$package_output" \
  -p:IsISLAMUDownstreamBuild=true -p:ContinuousIntegrationBuild=true --verbosity minimal

if [[ $1 == "--check" ]]; then
  committed_extract="$work_root/committed"
  rebuilt_extract="$work_root/rebuilt"
  mkdir -p "$committed_extract" "$rebuilt_extract"
  unzip -q "$package" -d "$committed_extract"
  unzip -q "$package_output/ISLAMU.Terminal.Gui.2.4.17-islamu.1.nupkg" -d "$rebuilt_extract"
  for extracted in "$committed_extract" "$rebuilt_extract"; do
    rm -rf -- "$extracted/package/services/metadata/core-properties"
    rm -f -- "$extracted/_rels/.rels" "$extracted/[Content_Types].xml"
  done
  diff -qr "$committed_extract" "$rebuilt_extract"
  dotnet run "$verifier" -- --check
  exit
fi

mkdir -p "$feed"
cp "$package_output/ISLAMU.Terminal.Gui.2.4.17-islamu.1.nupkg" "$package"

for project in \
  "$dependency_root/probe/TerminalGuiClosure.csproj" \
  "$repo_root/src/Event.SetupAssistant.Terminal/Event.SetupAssistant.Terminal.csproj" \
  "$repo_root/tests/Event.SetupAssistant.Terminal.Tests/Event.SetupAssistant.Terminal.Tests.csproj"
do
  NUGET_PACKAGES="$package_cache" dotnet restore "$project" --force-evaluate --verbosity minimal
done
NUGET_PACKAGES="$package_cache" dotnet run "$verifier" -- --write
NUGET_PACKAGES="$package_cache" dotnet run "$verifier" -- --check

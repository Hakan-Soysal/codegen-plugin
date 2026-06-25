#!/usr/bin/env bash
# Plugin'e gömülü generator (techgen) + conformance DLL'lerini src'ten yeniden üretir.
# TEK doğruluk kaynağı: hem CI (.github/workflows/pack-bundles.yml) hem insan bunu çağırır.
# Kullanım: bash scripts/pack-plugin-bundles.sh
set -euo pipefail
cd "$(dirname "$0")/.."

SKILL="plugins/codegen/skills/base-dotnet-rest"
TECHGEN="$SKILL/techgen"
CONF="$SKILL/conformance"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

# ContinuousIntegrationBuild → yol-normalize + deterministik(çe) çıktı (gereksiz diff'i azaltır).
echo "→ publish techgen (src/Gen.Cli)"
dotnet publish src/Gen.Cli/Gen.Cli.csproj -c Release -o "$TMP/techgen" \
  -p:ContinuousIntegrationBuild=true --nologo -v q

echo "→ publish conformance (conformance-adapter/Conformance.csproj)"
dotnet publish conformance-adapter/Conformance.csproj -c Release -o "$TMP/conf" \
  -p:ContinuousIntegrationBuild=true --nologo -v q

# Yalnız *.dll + *.json kopyala (deps/runtimeconfig). Glob; native apphost (.dll değil) + .pdb HARİÇ
# kalır → transitive bir NuGet bağımlılığı eklenirse otomatik taşınır (hardcode dosya listesi yok).
echo "→ refresh $TECHGEN"
rm -f "$TECHGEN"/*.dll "$TECHGEN"/*.json
cp "$TMP/techgen"/*.dll "$TMP/techgen"/*.json "$TECHGEN/"

echo "→ refresh $CONF"
rm -f "$CONF"/*.dll "$CONF"/*.json
cp "$TMP/conf"/*.dll "$TMP/conf"/*.json "$CONF/"

echo "✓ bundles refreshed from src"

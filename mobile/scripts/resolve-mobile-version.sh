#!/usr/bin/env bash
set -euo pipefail

raw="${1:-}"
if [ -z "$raw" ]; then
  echo "::error::Version argument is required." >&2
  exit 1
fi

version="${raw#[vV]}"
if ! [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$ ]]; then
  echo "::error::Version '$version' is not a valid SemVer value." >&2
  exit 1
fi

core="${version%%+*}"
numeric="${core%%-*}"
IFS=. read -r major minor patch <<< "$numeric"
major=$((10#$major)); minor=$((10#$minor)); patch=$((10#$patch))

if (( major > 2000 || minor > 99 || patch > 99 )); then
  echo "::error::Version '$version' exceeds bounds (major<=2000, minor<=99, patch<=99) for monotonic Android versionCode." >&2
  exit 1
fi

prerelease=false
slot=99
if [[ "$core" == *-* ]]; then
  prerelease=true
  pre="${core#*-}"
  if [[ "$pre" =~ \.([0-9]+)$ ]]; then
    pre_num=$((10#${BASH_REMATCH[1]}))
    if (( pre_num < 1 || pre_num > 98 )); then
      echo "::error::Prerelease number '$pre_num' in '$version' must be 1-98." >&2
      exit 1
    fi
    slot=$pre_num
  else
    echo "::error::Prerelease '$version' must end with a numeric sequence (e.g. -rc.1) so versionCode stays unique." >&2
    exit 1
  fi
fi

code=$((major * 1000000 + minor * 10000 + patch * 100 + slot))
if (( code < 1 || code > 2100000000 )); then
  echo "::error::Computed versionCode $code is outside Android's allowed range." >&2
  exit 1
fi

echo "version=$version"
echo "code=$code"
echo "tag=v$version"
echo "prerelease=$prerelease"

#!/usr/bin/env bash
# Release notes for the releases main.yml publishes, measured from the last
# TitanPlanner-* release. The fork's own commits are listed first; the
# ArduPilot/MissionPlanner commits brought in by upstream syncs follow in a
# collapsed section.
#
#   release-notes.sh <development|master> [notes.md] [ChangeLog.txt]
#
#   development  what this build has that the newest release doesn't, so the
#                notes read as the next release's would. That release need not
#                be an ancestor: master can carry commits development lacks,
#                such as README edits made on GitHub.
#   master       since the newest TitanPlanner-* tag that is an ancestor of HEAD,
#                skipping a tag on HEAD itself (a re-run of a released commit)
#
# notes.md is the GitHub release body; ChangeLog.txt is the plain-text copy the
# in-app updater links to. With no output files the Markdown is printed, so a
# local run previews the notes. Needs the full commit graph, the tags, and
# UPSTREAM_REF (default upstream/master) to split out upstream's commits.
set -euo pipefail

channel=${1:?usage: release-notes.sh <development|master> [notes.md] [ChangeLog.txt]}
md_out=${2:-}
txt_out=${3:-}
repo_url=${REPO_URL:-${GITHUB_SERVER_URL:-https://github.com}/${GITHUB_REPOSITORY:-Titan-Dynamics/TitanPlanner}}
upstream_ref=${UPSTREAM_REF:-upstream/master}
upstream_slug=${UPSTREAM_SLUG:-ArduPilot/MissionPlanner}
title=${RELEASE_TITLE:-}
max=150

head=$(git rev-parse HEAD)
base=
since_md=
since_txt=

# since <tag>: measure from that release
since() {
  base=$(git rev-parse "$1^{commit}")
  since_md="[${1/-/ }]($repo_url/releases/tag/$1)"
  since_txt=${1/-/ }
}

case $channel in
  development)
    tag=$(git tag -l 'TitanPlanner-*' --sort=-v:refname | head -n 1)
    [ -z "$tag" ] || since "$tag"
    ;;
  master)
    for tag in $(git tag -l 'TitanPlanner-*' --sort=-v:refname); do
      commit=$(git rev-parse "$tag^{commit}")
      if [ "$commit" != "$head" ] && git merge-base --is-ancestor "$commit" "$head"; then
        since "$tag"
        break
      fi
    done
    ;;
  *)
    echo "unknown channel: $channel" >&2
    exit 2
    ;;
esac

# "<sha>\t<subject>" per commit, oldest first
commits() { git log --no-merges --reverse --format='%H%x09%s' "$@"; }

range=("$head")
[ -n "$base" ] && range+=("^$base")
fork=()
upstream=()
if git rev-parse -q --verify "$upstream_ref^{commit}" >/dev/null; then
  mapfile -t fork < <(commits "${range[@]}" "^$upstream_ref")
  # Upstream commits HEAD now contains that base did not. Skipped for a first
  # release, where it would be the whole of upstream's history.
  merged=$(git merge-base --all "$head" "$upstream_ref" || true)
  if [ -n "$base" ] && [ -n "$merged" ]; then
    mapfile -t upstream < <(commits $merged "^$base")
  fi
else
  mapfile -t fork < <(commits "${range[@]}")
fi

# list <md|txt> <entries...>: one bullet per commit, capped at $max
list() {
  local format=$1 n=0 entry sha bullet=-
  shift
  [ "$format" = md ] || bullet='*'
  for entry in "$@"; do
    if [ $((n++)) -eq $max ]; then
      printf -- '%s ...and %d more, see the full diff\n' "$bullet" $(($# - max))
      break
    fi
    sha=${entry%%$'\t'*}
    if [ "$format" = md ]; then
      printf -- '- %s ([%s](%s/commit/%s))\n' "${entry#*$'\t'}" "${sha:0:7}" "$repo_url" "$sha"
    else
      printf -- '* %s (%s)\n' "${entry#*$'\t'}" "${sha:0:7}"
    fi
  done
}

# Commit subjects are not Markdown: keep List<T> and *.param literal
md_escape() { sed -e 's/&/\&amp;/g' -e 's/</\&lt;/g' -e 's/>/\&gt;/g' -e 's/\*/\\*/g'; }
# Upstream's "(#1234)" is an ArduPilot/MissionPlanner PR, not one of ours
md_upstream_refs() { sed -E "s,(^|[^[:alnum:]_/])#([0-9]+),\\1$upstream_slug#\\2,g"; }

if [ -z "$base" ]; then
  intro_md="First release."
  intro_txt=$intro_md
elif [ ${#fork[@]} -eq 0 ] && [ ${#upstream[@]} -eq 0 ]; then
  intro_md="No new commits since $since_md."
  intro_txt="No new commits since $since_txt."
else
  intro_md="Changes since $since_md."
  intro_txt="Changes since $since_txt."
fi
upstream_count="${#upstream[@]} commit"
[ ${#upstream[@]} -eq 1 ] || upstream_count+=s

if [ -n "$md_out" ]; then exec 3>"$md_out"; else exec 3>&1; fi
{
  echo "$intro_md"
  if [ ${#fork[@]} -gt 0 ]; then
    printf '\n### TitanPlanner\n\n'
    list md "${fork[@]}" | md_escape
  fi
  if [ ${#upstream[@]} -gt 0 ]; then
    printf '\n### Upstream Mission Planner\n\n'
    echo "Merged $upstream_count from [$upstream_slug](https://github.com/$upstream_slug)."
    printf '\n<details>\n<summary>Show upstream commits</summary>\n\n'
    list md "${upstream[@]}" | md_escape | md_upstream_refs
    printf '\n</details>\n'
  fi
  if [ -n "$base" ] && [ "$base" != "$head" ]; then
    printf '\n**Full diff:** [`%s...%s`](%s/compare/%s...%s)\n' "${base:0:7}" "${head:0:7}" "$repo_url" "$base" "$head"
  fi
} >&3

[ -n "$txt_out" ] || exit 0
{
  [ -n "$title" ] && printf '%s\n\n' "$title"
  echo "$intro_txt"
  if [ ${#fork[@]} -gt 0 ]; then
    printf '\nTitanPlanner\n'
    list txt "${fork[@]}"
  fi
  if [ ${#upstream[@]} -gt 0 ]; then
    printf '\nUpstream Mission Planner (%s)\n' "$upstream_count"
    list txt "${upstream[@]}"
  fi
  if [ -n "$base" ] && [ "$base" != "$head" ]; then
    printf '\nFull diff: %s/compare/%s...%s\n' "$repo_url" "$base" "$head"
  fi
} >"$txt_out"

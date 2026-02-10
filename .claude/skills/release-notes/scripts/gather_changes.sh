#!/usr/bin/env bash
# Gather all changes since a given git tag for release notes.
# Usage: gather_changes.sh <from-tag>
#
# Outputs:
#   1. Commit log (oneline) since the tag
#   2. Merged PRs since the tag (via gh CLI)
#   3. Diffstat summary

set -euo pipefail

FROM_TAG="${1:?Usage: gather_changes.sh <from-tag>}"

echo "===== Commits since ${FROM_TAG} ====="
git --no-pager log "${FROM_TAG}..HEAD" --oneline --no-merges

echo ""
echo "===== Merged PRs since ${FROM_TAG} ====="
TAG_DATE=$(git --no-pager log -1 --format='%aI' "${FROM_TAG}")
gh pr list --state merged --search "merged:>=${TAG_DATE}" --json number,title,labels --template \
  '{{range .}}#{{.number}} {{.title}}{{"\n"}}{{end}}' 2>/dev/null || echo "(gh CLI not available or no PRs found)"

echo ""
echo "===== Diffstat ====="
git --no-pager diff --stat "${FROM_TAG}..HEAD"

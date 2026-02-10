---
name: release-notes
description: "Generate mountaineering-themed release notes for MAUI Sherpa. Use when: (1) Creating release notes for a new version, (2) Drafting changelog entries, (3) Summarizing changes since the last release, (4) Writing version announcements. Collects all commits/PRs since the last published release tag, categorizes changes, and writes fun adventure-themed release notes."
---

# Release Notes

Generate release notes for MAUI Sherpa in a mountaineering/adventure theme.

## Workflow

1. **Determine version range** — Find the latest published release tag and collect all changes since then.
2. **Gather changes** — Run `scripts/gather_changes.sh` to get commits, PRs, and diff stats.
3. **Categorize changes** — Group into features, bug fixes, improvements, and infrastructure.
4. **Draft release notes** — Write the markdown following the style guide in `references/style-guide.md`.
5. **Save** — Write to `docs/release-notes/v{VERSION}.md`.

## Step 1: Determine Version Range

```bash
# Find the latest release tag
git describe --tags --abbrev=0

# If the user specifies a version, use that as the NEW version
# The range is: latest_tag..HEAD
```

Ask the user what the new version number is if not provided.

## Step 2: Gather Changes

Run the helper script to collect raw change data:

```bash
scripts/gather_changes.sh <from-tag>
```

This outputs commits, PRs (via `gh pr list`), and diffstat since the given tag. Review the output to understand the scope of changes.

## Step 3: Categorize Changes

Group changes into these categories (omit empty categories):

| Emoji | Category | Description |
|-------|----------|-------------|
| 🆕 | New Features | New pages, services, major capabilities |
| 🐛 | Bug Fixes | Fixes to existing functionality |
| ✨ | Improvements | UI polish, refactors, UX enhancements |
| 🔧 | Infrastructure | CI/CD, build, tooling, dependencies |

Each item gets a short mountaineering-flavored description. Not every commit needs its own line — group related commits into single entries.

## Step 4: Draft Release Notes

Read `references/style-guide.md` for the full style guide and example. Key rules:

- Title format: `## 🏔️ v{VERSION} — "{Subtitle}"`
- Subtitle is a pithy mountaineering metaphor for the release theme
- Opening paragraph ties the climbing metaphor to what this release delivers
- Each category section uses the emoji + category name as heading
- Items are concise but fun — one sentence, climbing pun optional
- Closing line in italics with a trail/summit metaphor and a relevant emoji

## Step 5: Save

Write the file to `docs/release-notes/v{VERSION}.md` and show the user the final result.

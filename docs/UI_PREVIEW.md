# Update Available Modal - UI Preview

## Modal Layout

```
┌─────────────────────────────────────────────────────────┐
│  🔼 Update Available                             ✕      │
├─────────────────────────────────────────────────────────┤
│                                                          │
│    ┌──────────────┐      →      ┌──────────────┐       │
│    │ CURRENT      │              │ NEW VERSION  │       │
│    │ VERSION      │              │              │       │
│    │   v1.0       │              │   v0.2.0     │       │
│    └──────────────┘              └──────────────┘       │
│                                                          │
│    v0.2.0 - Climbing Higher                             │
│    📅 Released Feb 07, 2026                             │
│                                                          │
│    What's New                                            │
│    ┌──────────────────────────────────────────────┐    │
│    │ ## 🏔️ v0.2.0 — "Climbing Higher"           │    │
│    │                                               │    │
│    │ ### New Features                              │    │
│    │ - Android SDK enhancements                    │    │
│    │ - Improved error handling                     │    │
│    │ - Performance optimizations                   │    │
│    │                                               │    │
│    │ ### Bug Fixes                                 │    │
│    │ - Fixed certificate sync issues               │    │
│    │ - Resolved emulator launch problems           │    │
│    └──────────────────────────────────────────────┘    │
│                                                          │
│                        ┌──────────┐  ┌──────────────┐  │
│                        │ Not Now  │  │ Download ⧉  │  │
│                        └──────────┘  └──────────────┘  │
└─────────────────────────────────────────────────────────┘
```

## Key UI Elements

### Header
- Icon: 🔼 (arrow up) in purple
- Title: "Update Available"
- Close button: ✕ in top-right corner

### Version Comparison Section
- Two badges side-by-side with arrow between
- **Current Version**: Shows "v1.0" (or current version)
- **New Version**: Shows new version in green
- Visual emphasis on upgrade path

### Release Information
- Release name/title
- Publication date with calendar icon
- Clear, readable typography

### Release Notes Section
- "What's New" heading
- Scrollable content area (max 300px height)
- Markdown formatted:
  - Headers (h1, h2, h3)
  - Lists (bullet and numbered)
  - Code blocks with syntax highlighting
  - Links in purple
  - Emojis supported

### Footer Actions
- **Not Now**: Gray button on left
  - Closes modal
  - User can continue using current version
- **Download Update**: Purple button on right
  - Opens GitHub release page in browser
  - Primary action, visually emphasized

## Color Scheme

- Background: Dark theme (--card-bg)
- Text Primary: White/light gray
- Text Secondary: Medium gray
- Accent: Purple (--accent-purple)
- Success: Green (for "new version" badge)
- Border: Subtle border color

## Animations

- **Fade In**: Modal backdrop fades in (0.2s)
- **Slide Up**: Modal content slides up (0.3s)
- **Hover Effects**: Buttons lift on hover
- **Smooth Transitions**: All interactions have smooth transitions

## Responsive Design

- Modal is 90% width, max 600px
- Max height 80vh (vertical height)
- Release notes scrollable if content exceeds 300px
- Works on all screen sizes

## Accessibility

- Semantic HTML structure
- Proper heading hierarchy
- Keyboard navigation support
- Click outside modal to dismiss
- ESC key support (via close button)

## Example Release Note Rendering

The modal uses Markdig to render GitHub's Markdown release notes, supporting:

```markdown
## 🏔️ v0.1.0 — "Picking Out Supplies"

Every great expedition starts at base camp...

### 🩺 MAUI Doctor
Before you hit the trail, you need a health check.

### 📦 Android SDK Management
Browse, search, and install Android SDK packages.

### 📱 Android Emulators
Spin up emulators, manage snapshots, tweak configurations.
```

This renders with:
- Proper heading sizes and weights
- List formatting
- Emoji support
- Links (clickable, in purple)
- Code blocks (if present)
- Bold and italic text

## User Flow

1. **App Starts** → Wait 2 seconds
2. **Background Check** → Query GitHub API
3. **If Update Available** → Show modal
4. **User Sees**:
   - Current vs. new version comparison
   - What's new in the release
   - Two clear options
5. **User Chooses**:
   - "Download Update" → Opens GitHub in browser
   - "Not Now" → Closes modal, continues using app

## Technical Notes

- Modal is only shown once per app session
- Network errors don't show modal (logged silently)
- Pre-releases and drafts are filtered out
- Only stable, published releases trigger modal
- Version comparison is semantic (1.0 < 1.1 < 2.0)

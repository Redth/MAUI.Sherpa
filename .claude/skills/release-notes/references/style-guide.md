# MAUI Sherpa Release Notes Style Guide

## Voice & Tone

MAUI Sherpa release notes are written as a mountaineering adventure narrative. The app is a "sherpa" guiding .NET MAUI developers up the mountain. Each release is a stage of the expedition.

**Key principles:**
- Fun but informative — the reader should learn what changed AND smile
- Climbing metaphors tie to dev tool concepts (gear = SDKs, trail = workflow, summit = shipping)
- Keep it concise — each item is one or two sentences max
- Don't force puns — if a natural metaphor fits, use it; if not, just be clear and lively

## Document Structure

```markdown
## 🏔️ v{VERSION} — "{Subtitle}"

{Opening paragraph: 2-3 sentences connecting the expedition metaphor to the release theme.
What stage of the climb are we at? What does this release prepare us for?}

### {Emoji} {Category Name}
{Brief thematic intro sentence for the category, optional.}

- **{Feature/Fix Name}** — {One-sentence description with optional climbing flavor.}
- **{Feature/Fix Name}** — {Description.}

### {Emoji} {Category Name}
- **{Item}** — {Description.}

---

*{Closing line: italic, reflective, looks ahead to the next stage of the climb. End with a relevant emoji.}*
```

## Subtitle Convention

The subtitle is a short phrase in quotes that captures the theme of the release as a stage of mountaineering:

- v0.1.0 — "Picking Out Supplies" (initial tooling setup)
- Future examples: "Leaving Base Camp", "Crossing the Icefall", "Setting Up Camp II", "The Final Push", "Ridge Walking", "Rope Team", "Acclimatization Day"

Pick something that metaphorically matches the nature of the changes (e.g., a stability release = "Acclimatization Day", a big feature drop = "Crossing the Icefall").

## Category Headings

Use the emoji prefix on category headings:

| Heading | Use for |
|---------|---------|
| `### 🆕 New Features` | New pages, major capabilities, new service integrations |
| `### 🐛 Bug Fixes` | Corrections to existing behavior |
| `### ✨ Improvements` | UI polish, refactors, performance, UX tweaks |
| `### 🔧 Infrastructure` | CI/CD, build pipeline, dependency bumps, tooling |

Omit any category that has no items. Order categories by impact: features first, then improvements, bug fixes, infrastructure.

## Writing Style Examples

**Good — natural metaphor:**
> **iOS Simulator Inspector** — A new tabbed inspector window lets you peek inside running simulators: apps, tools, and device info, all in one basecamp overview.

**Good — no forced pun, still lively:**
> **Android Keystore Management** — Create, inspect, and export Android keystores with PEPK support for Play Store uploads.

**Bad — forced/cringe:**
> **Fixed a bug** — We summited the bug mountain and planted our flag of victory on the peak of fixed code! 🏳️

**Bad — too dry:**
> **Added keystore management.** Fixed several bugs.

## Closing Line Examples

> *The trail is getting steeper, but the view keeps getting better.* 🌄

> *Base camp is behind us now — time to start climbing.* ⛏️

> *We're still lacing up our boots, but the summit is already in sight.* 🥾

## Full Example (v0.1.0)

```markdown
## 🏔️ v0.1.0 — "Picking Out Supplies"

Every great expedition starts at base camp, rummaging through gear and making sure you've
got everything you need before the climb. Consider **MAUI Sherpa** your trusty outfitter
for .NET MAUI development — helping you inventory, organize, and prepare all your dev tools
so you're not stuck halfway up the mountain wondering where you left your crampons.

This first release is all about getting the pack loaded up:

### 🩺 MAUI Doctor
Before you hit the trail, you need a health check. Doctor scans your development
environment — .NET SDKs, workloads, dependencies — and tells you what's missing, what's
broken, and offers to fix it on the spot. Think of it as your pre-climb physical.

### 📦 Android SDK Management
Browse, search, and install Android SDK packages without ever opening a terminal. Platform
tools, build tools, system images — all the gear, neatly organized on the shelf.

### 📱 Android Emulators
Spin up emulators, manage snapshots, tweak configurations. Your virtual base camps, ready
when you are.

### 🍎 Apple Developer Tools *(macOS)*
Certificates, provisioning profiles, bundle IDs, device registration — the bureaucratic
paperwork of Apple development, made slightly less painful. Like filling out your climbing
permits, but with fewer forms.

### 🤖 GitHub Copilot Integration
An AI sherpa for your sherpa. Ask questions, get help with environment setup, and receive
suggestions — right inside the app.

### 🏠 Dashboard
Your base camp overview. One glance at the state of your environment before you set out.

---

*We're still at the trailhead — the summit is a long way off. But hey, you can't climb
Everest without first picking out the right pair of boots.* 🥾
```

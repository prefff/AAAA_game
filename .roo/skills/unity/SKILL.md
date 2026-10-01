---
name: unity
description: |
  Unity Agent Plugin integration for this project.
  Use for any Unity-specific task: driving a live Unity Editor, installing editors,
  managing packages, building, testing, source control, scene/GameObject manipulation,
  sprite atlases, URP setup, UI, audio, physics, multiplayer, IAP, ads, and more.
allowed-tools:
  - Bash
---

# Unity (project skill wrapper)

This skill is a lightweight wrapper that loads the full [`unity-cli`](unity-cli/SKILL.md) skill.

## When to use this skill

Use `unity` (this file) when you want the model to know this is a Unity project and to apply the Unity Agent Plugin knowledge. It automatically delegates to the [`unity-cli`](unity-cli/SKILL.md) skill, which contains the actual command reference, workflows, and editor-control instructions.

## How to use

1. Always prefer driving the live Unity Editor via the Unity CLI when one is available for this project.
2. If no Editor is reachable, follow the fallbacks in [`unity-cli/SKILL.md`](unity-cli/SKILL.md).
3. For domain-specific tasks (sprite atlases, URP, UI, etc.), read the dedicated skill files under `.roo/skills/`.

## Delegated skills

- [`unity-cli`](unity-cli/SKILL.md) — CLI, live Editor control, builds, tests, source control.
- Add more sibling skill folders here as needed (e.g. `manage-sprite-atlas`, `ui`, `urp-postprocessing`).

## Project context

- Unity version: 6000.2.8f1
- Render pipeline: URP (`com.unity.render-pipelines.universal` 17.0.3)
- Input: Input System 1.11.2
- Pipeline package (`com.unity.pipeline`) is not yet installed; install it with `unity pipeline install --project-path <project>` if you want live Editor control.

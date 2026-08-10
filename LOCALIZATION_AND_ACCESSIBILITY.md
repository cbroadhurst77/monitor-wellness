# Localisation and sensory-accessibility implementation standard

Monitor Wellness currently ships English-only. This document defines the work required before
claiming support for any additional language or accessibility conformance level.

## Localisation

1. Move every user-visible XAML and C# string into culture-specific resource files. Never
   machine-translate medical, safety, recovery, privacy, or installer wording without
   professional review.
2. Keep persisted data and protocol values culture-invariant: JSON property names, CSV schema,
   device paths, process names, hotkeys, timestamps, and numeric parsing must not change when
   the UI language changes.
3. Use the current UI culture only for presentation: dates, time, numbers, plural forms, and
   display text.
4. Test English pseudo-localisation (expansion, brackets, accented characters) for clipped
   controls before adding a translation. Then test each real locale in context at 100%, 200%,
   and 300% scaling.
5. Introduce a locale selector only once at least one complete, professionally reviewed
   translation exists. An incomplete selector is worse than a clearly English-only product.

## Sensory-safe interaction

- Never use flashing, pulsing, red-alert animation, or rapid visual transitions to convey an
  essential state. Recovery and safety actions must also have text and keyboard feedback.
- Preserve the gradual migraine-mode exit even when ordinary UI animation is reduced; it is a
  display-safety transition rather than decorative motion.
- Keep emergency recovery keyboard-accessible and independent of overlays, screen readers,
  mouse input, and network availability.
- Every new window must have a descriptive title, usable keyboard focus order, a visible close
  route, automation names for non-obvious controls, and readable light/dark/high-contrast
  treatment.
- Test keyboard-only navigation, Narrator, Windows contrast themes, Windows colour filters,
  text scaling, reduced motion, and 200% DPI on the exact release build.

## Acceptance evidence per release

- Automated markup tests for newly added WPF controls.
- Keyboard and Narrator test notes with the Windows version used.
- Screenshots or recordings showing no unintended flash during settings, topology changes,
  application switching, and emergency recovery.
- Translator and in-context linguistic QA approval for each advertised language.

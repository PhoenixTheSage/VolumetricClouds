# VolumetricClouds changelog

## 1.2.1 — 2026-10-14

- Register the cloud Status page independently of settings and pack startup; consolidate diagnostic controls there.
- Include terminal-registration health in cloud status reports.
- Replace empty Publicizer access grants with the declared game assembly names.

## 1.2.0 — previous release

Release 1.2.0 Nubis studio, climate, and guest volumes

Version: 1.1.0 -> 1.2.0 (minor). The assembly stamp that was still 1.0.0 moves with this release.

Changelog:
- Replace the live view with Volume Studio: one sun-lit cumulus while the planetary shell march stays off. Studio controls live under Anomaly Shaders.
- Sample density as a Nubis dimensional profile: 128^3 Perlin-Worley plus 64^3 Worley erosion, in local east/north metres.
- Step a 64x64 climate field at 4 Hz, with storm kernels advected on the zonal and meridional wind.
- Add sun-shadow stamp and subtract passes, temporal history, and the guest-bus renderer route.
- Ship the v4 and v5 shape and detail noise tiles used by that profile.

Co-authored-by: Cursor <cursoragent@cursor.com>

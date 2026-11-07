# Moodium Opening — Unity / Vision Pro Material Setup

## Import baseline

- Recommended renderer: URP with PolySpatial validation enabled.
- FBX materials are placeholders only. Create Unity `.mat` assets and bind the PNG files from `Textures_Baked` manually.
- Base Color and Emission textures: `sRGB` enabled.
- Normal, Metallic and Roughness textures: `sRGB` disabled.
- `Logo_ParticleTargets` is data geometry. Disable its Mesh Renderer at runtime.

## Candy

Shader: `Universal Render Pipeline/Lit`

- Surface Type: `Transparent`
- Blending Mode: `Alpha`
- Render Face: `Both` when the inner wall must remain visible
- Base Map: `Textures_Baked/Candy/BaseColor.png`
- Base Map alpha: embedded in BaseColor; starting value is approximately `0.38`
- Metallic Map: `Metallic.png`
- Normal Map: `Normal.png`
- Roughness: Unity Lit expects Smoothness. Use `Smoothness = 1 - Roughness`; the supplied Roughness value is approximately `0.12`, so start near `0.88` Smoothness.
- Emission Map: `Emission.png`; enable Emission and tint pink/lavender.

The Blender Candy uses Layer Weight, ColorRamp, Transparent BSDF, Transmission and view-dependent iridescence. FBX cannot transfer this node graph.

**Unity需要重新制作该效果。**

For the closest Vision Pro result, recreate a lightweight Fresnel/rim color in a PolySpatial-compatible Shader Graph. Keep the supplied maps as the stable fallback. Do not depend on Blender Transmission or compositor glow.

## Sprite

Preferred shader: `URP/Lit` for a softly lit character, or `URP/Unlit` when consistent brightness is more important.

- Base Map: `Textures_Baked/Sprite/BaseColor.png`
- Emission Map: `Textures_Baked/Sprite/Emission.png`
- Enable Emission and tune HDR intensity on device.
- Use Opaque unless a separate dissolve/reveal effect is required.

The animation includes `UnityAlpha` as authoring metadata, but FBX does not automatically connect that property to a Unity material. Drive reveal alpha or dissolve from the Animator/Timeline script.

**Unity需要重新制作该效果。**

## Logo

Shader: `Universal Render Pipeline/Lit`

- Base Map: `Textures_Baked/Logo/BaseColor.png`
- Emission Map: `Textures_Baked/Logo/Emission.png`
- Surface Type: Opaque for the stable final state; Transparent only during the reveal if required.
- Enable Emission and animate an emission multiplier during `Logo_Appear`.

The Blender compositor Fog Glow is not contained in FBX. For PolySpatial, prefer emission plus a lightweight duplicate halo mesh or small particle layer instead of relying on screen-space post-processing.

**Unity需要重新制作该效果。**

## Runtime animation hooks

The Blender source contains custom authoring properties named `UnityGlow`, `UnityAlpha`, `EmitTrail`, and `ParticleGather`. Treat them as timing references. In Unity, connect the animation state normalized time or Animation Events to:

- Candy emission multiplier
- Sprite reveal alpha/dissolve
- Trail Particle System emission
- Logo gather Particle System and final Logo emission

## Transparency notes for Vision Pro

- Transparent overlapping surfaces are expensive and can sort differently from Blender.
- Keep wrapper fragments and transparent particle count low.
- Use simple Collider proxies; do not use the transparent render mesh as a MeshCollider.
- Verify the final shader through PolySpatial Project Validation and on the Vision Pro device.

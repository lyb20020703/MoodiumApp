# Moodium Opening Unity Asset Export Report

## Delivery root

`/Users/yibei/Desktop/appcontest2026/logoani`

## Exported files

### Blender source

- `Blender_Source/Moodium_Opening_Final.blend` — final organized scene, 60 fps, frames 1–360.

### FBX

- `FBX/Candy_Animation.fbx`
  - Objects: Candy root, Candy_Idle, Candy_Core, Candy_Inside, Wrapper_Left, Wrapper_Right, Candy_Transformation.
  - Relevant animation: Candy_Idle, Candy_Open_Core, Candy_Open_Wrapper_Left, Candy_Open_Wrapper_Right.
- `FBX/Sprite_Animation.fbx`
  - Objects: Sprite root, Sprite_Idle, Sprite_Fly, Sprite_Runtime.
  - Relevant animation: Sprite_Reveal, Sprite_Idle, Sprite_Fly.
- `FBX/Moodium_Logo.fbx`
  - Objects: Logo root, Logo_Appear, Logo_Final, Logo_ParticleTargets.
  - Relevant animation: Logo_Appear and Logo_Final_Alpha.
- `FBX/Moodium_Opening_All.fbx`
  - Complete Candy + Sprite + Logo hierarchy and all animation actions.

FBX settings used:

- Apply Transform: On
- Apply Modifiers: On
- Forward: `-Z Forward`
- Up: `Y Up`
- Bake Animation: On
- NLA Strips: On
- All Actions: On
- Bake step: 1 frame
- Simplify: 0

### Baked textures

- Candy: BaseColor with alpha, Normal, Metallic, Roughness, Emission — all 2048×2048 PNG.
- Sprite: BaseColor and Emission — 2048×2048 PNG.
- Logo: BaseColor and Emission — 2048×2048 PNG.

## Scene checks and fixes

- Candy, wrappers, Sprite and Logo are independent runtime objects.
- Wrapper origins remain at the left/right candy necks for opening animation.
- Sprite origin is centered and its runtime mesh is reduced to approximately 31.9k faces.
- Runtime objects contain no unapplied Blender modifiers.
- The Logo is a Mesh and faces the same user direction as the Candy.
- `Logo_Final动作` was renamed to `Logo_Final_Alpha`.
- Sprite's external JPG dependency was consolidated into the delivery PNG.
- Original high-poly/source objects remain hidden in the Blender source with `SOURCE_` prefixes.

## Unity import steps

1. Copy the complete `logoani` folder under the Unity project's `Assets` directory.
2. Select each FBX and enable `Import Animation`.
3. Use Rig type `Generic` or `None`; these are transform animations, not humanoid animation.
4. Inspect the imported Takes/Clips. The FBX files were exported with All Actions and NLA Strips enabled.
5. For the complete sequence, use `Moodium_Opening_All.fbx`. For modular Animator states, use the three separated animation FBXs.
6. Create Unity materials following `Unity_Materials/Material_Setup_Guide.md` and assign the baked PNG maps.
7. Disable the Renderer on `Logo_ParticleTargets`; read its vertices as particle convergence targets.
8. Add a simplified Box/Capsule collider to the Candy interaction root.
9. Trigger the Animator from the Vision Pro hand/spatial pointer interaction script.

## Material transfer status

Can be restored from textures:

- Sprite Base Color
- Logo gradient Base Color
- Candy representative Base Color, Normal, Metallic, Roughness and Emission fallback

Must be manually rebound in Unity:

- All FBX material slots to their Unity `.mat` assets
- Candy BaseColor alpha and Transparent surface settings
- Candy Fresnel/iridescent rim behavior
- Sprite emission and reveal alpha/dissolve
- Logo emission multiplier

Cannot transfer directly through FBX:

- Blender Layer Weight / ColorRamp behavior
- Transparent BSDF and Blender Transmission
- Blender compositor Fog Glow
- Custom material animation driven only by Blender node inputs
- Particle trail and Logo gather runtime logic

For these items: **Unity需要重新制作该效果。**

## Animation timing

- Candy Idle: frames 1–120
- Candy Open: frames 121–144
- Sprite Reveal: frames 145–175
- Sprite Fly: frames 170–340
- Logo Appear: frames 280–335
- Full opening: frames 1–360 at 60 fps

The wrappers and Candy Core are already gone before the Sprite reveal becomes visible.

## Vision Pro runtime notes

- Transparent materials and overlapping fragments create overdraw; keep fragment and particle counts conservative.
- Use PolySpatial-compatible URP Lit/Unlit or Shader Graph materials and run Project Validation.
- Do not assume desktop URP post-processing or Blender glow will look identical on visionOS.
- Use built-in Particle System with PolySpatial-supported particle materials as the primary trail/gather implementation.
- Profile on device; the simulator does not fully represent hardware input or rendering behavior.
- Use collider proxies for touch/pinch interaction rather than high-poly MeshColliders.

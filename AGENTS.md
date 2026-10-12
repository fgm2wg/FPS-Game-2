# AGENTS.md — FPS Game Architecture & Development Guidelines

> **CRITICAL REPOSITORY DIRECTIVE**:
> **NEVER run `git add`, `git commit`, `git push`, or any destructive Git commands (`git reset`, `git checkout`, `git clean`)!**
> The repository owner exclusively handles all Git staging, commits, branch management, and remote operations.
> Agents are strictly restricted to reading, authoring, and modifying project workspace files.

---

## 1. Engine & Environment Specification

- **Engine**: Godot Engine 4.7+ (Forward+ Renderer, Direct3D 12 on Windows)
- **Physics Engine**: Godot Jolt Physics 3D
- **Programming Language**: C# (.NET 8/9, C# 12+) — **Strictly NO GDScript** (unless explicitly required by an imported third-party engine plugin)
- **Target Genre**: High-performance First-Person Shooter (FPS)
- **Primary Pillars**: 
  1. **Multiplayer-First Architecture**: Authoritative server with client prediction, built on Godot `ENetMultiplayerPeer` (no single-player retrofits).
  2. Deterministic, responsive gunplay & movement
  3. CPU & GPU optimization-first architecture
  4. Clean, modular component-based architecture
  5. Testable codebases with headless automated testing support

> **MASTER IMPLEMENTATION PLAN**:
> For the complete phased development roadmap and milestone deliverables, consult **`ROADMAP.md`**.

---

## 2. Directory Layout & Placement Blueprint

The project follows a clean separation between raw/imported engine assets (`assets/`), gameplay code & scenes (`src/`), game balance/definitions (`data/`), and test suites (`tests/`).

```
FPS-Game-2/
├── assets/                          # Engine-imported raw assets only
│   ├── audio/                       # Audio files (.wav, .ogg)
│   │   ├── music/                   # Background music & ambient loops
│   │   ├── sfx/                     # Sound effects
│   │   │   ├── footsteps/           # Surface-specific footstep audio
│   │   │   ├── impacts/             # Bullet & melee impacts per surface
│   │   │   ├── ui/                  # Interface feedback sounds
│   │   │   └── weapons/             # Gunfire, reloads, cocking, dry fires
│   │   └── voices/                  # Character dialogue, grunts, callouts
│   ├── fonts/                       # UI typography (.ttf, .otf)
│   ├── materials/                   # Godot Material resources (.tres, .material)
│   │   ├── environment/             # World materials
│   │   ├── physical_surfaces/       # Surface definitions (wood, metal, flesh, concrete)
│   │   └── weapons/                 # Weapon materials
│   ├── models/                      # 3D meshes & rigs (.glb, .gltf)
│   │   ├── characters/              # Player & enemy rigs/meshes
│   │   ├── environment/             # Modular buildings, terrain, architecture
│   │   ├── props/                   # Crates, barrels, furniture, pickups
│   │   ├── vfx/                     # 3D meshes for particles/projectiles
│   │   └── weapons/                 # First-person viewmodels & third-person worldmodels
│   ├── shaders/                     # Custom visual shaders (.gdshader)
│   │   ├── environment/             # Water, foliage wind, triplanar shaders
│   │   ├── post_process/            # Full-screen passes (outline, motion blur, vignette)
│   │   └── vfx/                     # Dissolve, shields, heat distortion
│   └── textures/                    # 2D source textures (.png, .webp, .exr)
│       ├── characters/
│       ├── decals/                  # Bullet holes, blood stains, scorch marks
│       ├── environment/
│       ├── particles/               # Smoke, fire, sparks, sparks sheets
│       └── weapons/
│
├── src/                             # Game logic, scenes, and C# source
│   ├── audio/                       # Audio management & bus architecture
│   │   ├── buses/                   # DefaultBusLayout.tres and bus presets
│   │   └── systems/                 # SoundManager.cs, SpatialAudioEmitter3D.cs, SoundPool.cs
│   ├── common/                      # Reusable components & patterns across entities
│   │   ├── components/              # Node-composition building blocks
│   │   │   ├── combat/              # HitboxComponent.cs, HurtboxComponent.cs
│   │   │   ├── health/              # HealthComponent.cs, ShieldComponent.cs
│   │   │   ├── interaction/         # Interactable.cs, Interactor.cs
│   │   │   └── movement/            # VelocityComponent.cs, GroundDetector.cs
│   │   └── state_machine/           # StateMachine.cs, State.cs, IState.cs
│   ├── core/                        # Engine core, singletons, utilities, plumbing
│   │   ├── autoload/                # Global singletons (GameManager, EventBus)
│   │   ├── events/                  # Strongly-typed event payloads & bus
│   │   ├── pooling/                 # Generic ObjectPool.cs, NodePool.cs
│   │   └── utils/                   # MathUtils.cs, RaycastUtils.cs, Profiling.cs
│   ├── entities/                    # Concrete game actors
│   │   ├── enemies/                 # Enemy controllers, AI agents, spawners
│   │   ├── player/                  # Player scenes, controller, camera, viewmodel
│   │   │   ├── camera/              # CameraController.cs, HeadBob.cs, CameraFOV.cs
│   │   │   ├── controllers/         # PlayerController.cs, PlayerInput.cs
│   │   │   ├── network/             # LocalPlayerController.cs, RemotePlayerProxy.cs
│   │   │   ├── states/              # IdleState.cs, MoveState.cs, SprintState.cs, SlideState.cs
│   │   │   └── viewmodel/           # ViewmodelController.cs, WeaponSway.cs, ViewmodelRig.tscn
│   │   └── props/                   # Interactive, destructible, or physical props
│   ├── gamemodes/                   # Game rules & match lifecycle
│   │   ├── base/                    # BaseGameMode.cs, TeamDefinition.cs
│   │   └── tdm/                     # TeamDeathmatchMode.cs, ScoreTracker.cs
│   ├── levels/                      # Scenes representing gameplay environments
│   │   ├── base/                    # BaseLevel.cs, LevelManager.cs
│   │   ├── maps/                    # Production levels
│   │   ├── templates/               # Default lighting, environment rigs, skyboxes
│   │   └── world_gym/               # Test environments (movement gym, ballistics range, AI arena)
│   ├── network/                     # Multiplayer networking & replication
│   │   ├── core/                    # NetworkManager.cs (Autoload), ENetConfig.cs
│   │   ├── data/                    # NetworkPlayerInfo.cs, NetTransform.cs
│   │   └── spawning/                # NetworkSpawner.cs (MultiplayerSpawner setups)
│   ├── ui/                          # User interface scenes & scripts
│   │   ├── components/              # Reticle, health bar, ammo counter, damage indicators
│   │   ├── hud/                     # In-game HUD root (HUD.tscn, scoreboard/)
│   │   ├── menus/                   # MainMenu, lobby/ (ConnectionMenu.tscn), Settings
│   │   └── themes/                  # UI themes & styles (.tres)
│   ├── vfx/                         # Visual effect scenes and pool managers
│   │   ├── decals/                  # BulletImpactDecal.tscn, DecalManager.cs
│   │   ├── impacts/                 # Surface-specific particle burst scenes
│   │   └── particles/               # MuzzleFlash.tscn, ShellCasing.tscn
│   └── weapons/                     # Weapons subsystem
│       ├── base/                    # WeaponBase.cs, IWeapon.cs, WeaponManager.cs
│       ├── data/                    # WeaponData.cs, AmmoData.cs, RecoilData.cs
│       ├── hitscan/                 # HitscanWeapon.cs, BulletTracer.cs
│       ├── projectiles/             # BallisticProjectile.cs, ProjectileManager.cs
│       └── recoil/                  # ProceduralRecoil.cs, SpringInterpolator.cs
│
├── data/                            # Custom Godot Resource instances & configurations
│   ├── config/                      # Default user settings, keybindings defaults
│   └── resources/                   # .tres data assets configured via C# [GlobalClass]
│       ├── items/                   # Pickup definitions
│       ├── surfaces/                # PhysicsSurfaceData.tres (friction, bullet impacts, sounds)
│       └── weapons/                 # Pistol.tres, Rifle.tres, Shotgun.tres
│
└── tests/                           # Automated & manual test suites
    ├── integration/                 # Headless Godot node & physics integration tests
    ├── scenes/                      # Test runner scenes & performance stress benchmarks
    │   └── benchmarks/              # 100-AI stress test, 10,000-projectile benchmark
    └── unit/                        # Pure C# logic tests (NUnit / xUnit compatible)
        ├── combat/                  # Damage falloff, armor calculation tests
        ├── core/                    # Pooling checkout/recycle, event bus tests
        └── weapons/                 # Recoil curves, spread calculations, ammo math tests
```

### File Placement Rules:
1. **Never dump scripts in the root directory.**
2. **Scenes (`.tscn`) and their primary C# script (`.cs`) live together** when they are entity-specific (e.g., `src/entities/player/Player.tscn` and `src/entities/player/controllers/PlayerController.cs`).
3. **Reusable components** live in `src/common/components/` and are attached to entities via composition.
4. **Data drives gameplay**: Weapon stats, ammo curves, surface acoustic responses, and damage multipliers MUST be defined in `[GlobalClass]` C# `Resource` files placed in `data/resources/`, NOT hardcoded inside controller scripts.
5. **No binary art or raw assets in `src/`**: All 3D models, textures, sounds, and shaders belong under `assets/`.

---

## 3. Collision Matrix & Physics System (Jolt 3D)

Godot provides 32 collision layers. Precise layer assignment is essential for high tick-rate FPS games to avoid unnecessary broadphase collision checks and prevent raycast hitreg bugs.

### 3.1 Collision Layer Blueprint

| Layer # | Layer Name | Purpose | Collides With (Mask) |
|---|---|---|---|
| **1** | `World_Static` | Architecture, walls, floors, ceilings, static terrain | `Player_Body`, `Enemy_Body`, `World_Dynamic`, `Projectiles_Player`, `Projectiles_Enemy`, `Ragdolls_Debris` |
| **2** | `World_Dynamic` | Physics crates, pushables, dynamic doors, movable props | `World_Static`, `World_Dynamic`, `Player_Body`, `Enemy_Body`, `Projectiles_Player`, `Projectiles_Enemy` |
| **3** | `Player_Body` | Player navigation capsule (`CharacterBody3D`) for world movement | `World_Static`, `World_Dynamic`, `Enemy_Body`, `Triggers_Interaction`, `Area_Damage` |
| **4** | `Enemy_Body` | Enemy navigation capsule (`CharacterBody3D`) for world movement | `World_Static`, `World_Dynamic`, `Player_Body`, `Enemy_Body`, `Area_Damage` |
| **5** | `Player_Hitbox` | Precision hurtboxes attached to Player skeleton bones (Head, Torso, Limbs) | `Projectiles_Enemy` *(Receives enemy damage; does NOT collide with world!)* |
| **6** | `Enemy_Hitbox` | Precision hurtboxes attached to Enemy skeleton bones (Head, Torso, Limbs) | `Projectiles_Player` *(Receives player damage; does NOT collide with world!)* |
| **7** | `Projectiles_Player` | Player bullets, rockets, buckshot pellets, raycasts | `World_Static`, `World_Dynamic`, `Enemy_Hitbox`, `Environment_Destructible`, `Shields_Barriers` |
| **8** | `Projectiles_Enemy` | Enemy bullets, rockets, raycasts | `World_Static`, `World_Dynamic`, `Player_Hitbox`, `Environment_Destructible`, `Shields_Barriers` |
| **9** | `Triggers_Interaction` | Interactable items, buttons, doors, ladder volumes (`Area3D`) | `Player_Body`, `Viewmodel_Raycast` |
| **10** | `Viewmodel_Raycast` | Player crosshair interaction & pickup raycast queries | `World_Static`, `World_Dynamic`, `Triggers_Interaction`, `Pickups` |
| **11** | `Ragdolls_Debris` | Dead bodies, cosmetic gibs, spent shell casings (`RigidBody3D`) | `World_Static` *(Strictly isolated! Must never test against players or bullets)* |
| **12** | `Occlusion_Sight` | AI line-of-sight & visibility test raycasts | `World_Static`, `World_Dynamic`, `Player_Body` |
| **13** | `Area_Damage` | Environmental hazards: fire, toxic acid, explosive splash radius | `Player_Body`, `Enemy_Body`, `Environment_Destructible` |
| **14** | `Pickups` | Collectible weapons, health packs, ammo boxes | `Player_Body`, `World_Static` |
| **15** | `Shields_Barriers` | Energy shields, bulletproof glass, deployable covers | `Projectiles_Player`, `Projectiles_Enemy` |
| **16** | `Environment_Destructible` | Breakable glass, wooden pallets, exploding barrels | `World_Static`, `Projectiles_Player`, `Projectiles_Enemy`, `Area_Damage` |
| **17–32** | *Reserved* | Reserved for team factions, vehicles, water volumes, and custom expansions | As needed |

### 3.2 Critical Collision Best Practices
- **Body Capsule vs. Hitbox Separation**:
  - `CharacterBody3D` capsule is **exclusively for movement**. Bullets and raycasts MUST NEVER hit Layer 3 or 4.
  - Bullets and raycasts exclusively test against **Layer 5 (`Player_Hitbox`)** and **Layer 6 (`Enemy_Hitbox`)**.
  - Hitbox `Area3D` nodes are parented to bone attachments with custom multipliers: Head (2.5x), Chest (1.0x), Limbs (0.7x).
- **Debris Isolation**:
  - Spent brass casings, debris chunks, and ragdolls must only mask against Layer 1 (`World_Static`). Checking dynamic props or entities for 50 flying casings destroys physics CPU performance.
- **Continuous Collision Detection (CCD)**:
  - For high-speed projectiles (rockets, grenades), enable CCD or use swept sphere/ray raycasting to prevent tunneling through thin walls.

---

## 4. Optimization-First Mandate (CPU & GPU)

Optimization is not a polish phase; it is an architectural requirement. Every script and scene must adhere to these rules from the first line of code.

### 4.1 CPU & C# Memory Management (Zero-Allocation Rule)

1. **Zero Garbage Collection (GC) in Hot Loops**:
   - `_Process(double delta)` and `_PhysicsProcess(double delta)` must produce **ZERO heap allocations**.
   - **Banned in hot loops**: `new` object, `new List<T>()`, LINQ queries (`.Where()`, `.Select()`, `.ToList()`), string concatenations (`+`, `$"..."`), boxing (`object` casting), and lambda closures.
   - Use `readonly ref struct` or `struct` for math, damage packets, and trajectory calculations.
   - Use `Span<T>`, `ReadOnlySpan<T>`, and pre-allocated buffers.
2. **Object Pooling (`NodePool<T>`)**:
   - **Never call `QueueFree()` or `Instantiate()` at runtime** for recurrent objects:
     - Projectiles (bullets, rockets, pellets)
     - Impact decals (bullet holes, scorch marks)
     - Particle bursts (sparks, blood splatter, smoke)
     - Spent shell casings
     - Sound effect audio emitters
   - Pre-warm pools in `src/core/pooling/` during level loading and reuse with reset methods.
3. **Physics Raycasting Optimization**:
   - Never instantiate a new `PhysicsRayQueryParameters3D` every frame.
   - Cache and reuse query parameter instances:
     ```csharp
     // Pre-allocated cached query in class scope
     private PhysicsRayQueryParameters3D _rayQuery;
     
     // In _Ready()
     _rayQuery = PhysicsRayQueryParameters3D.Create(Vector3.Zero, Vector3.Zero, CollisionMasks.PlayerHitscan);
     
     // In hot loop: mutate existing query struct
     _rayQuery.From = origin;
     _rayQuery.To = destination;
     var result = spaceState.IntersectRay(_rayQuery);
     ```
4. **Data-Oriented Projectile Simulation**:
   - For fast-firing automatic weapons (e.g. 600–1200 RPM) or shotguns (12 pellets/shot), do NOT spawn 100 individual `Node3D` scenes.
   - Simulate active bullets in a single contiguous C# array inside `ProjectileManager.cs` updating positions via basic kinematic equations, and render them using a single `MultiMeshInstance3D`.
5. **AI Tick Throttling**:
   - Never update AI navigation paths (`NavigationAgent3D.TargetPosition`) every physics frame.
   - Throttle path queries to 5–10 Hz using timers or round-robin scheduler.
   - Put distant or off-screen enemies into low-frequency update tiers.

### 4.2 GPU & Rendering Architecture

1. **Culling Strategy**:
   - **Frustum Culling**: Ensure mesh AABB bounding boxes are strictly accurate.
   - **Occlusion Culling**: Generate and bake `OccluderInstance3D` / `ArrayOccluder3D` for all level geometry and large structures.
   - **Distance Culling & LOD (Level of Detail)**:
     - Configure `visibility_range_begin` and `visibility_range_end` on all environment props.
     - Leverage Godot's automatic mesh LOD generation on import.
     - Small clutter props must fade out at 25–40 meters.
2. **Draw Call Batching**:
   - Repetitive props (debris, rocks, foliage, street lamps, wall panels, shell casings) must use **`MultiMeshInstance3D`**.
   - Share materials across modular environment pieces to allow the Forward+ renderer to batch draw calls efficiently.
3. **Lighting & Shadows**:
   - Max 1 dynamic `DirectionalLight3D` with Cascaded Shadow Maps (CSM).
   - Tightly restrict `OmniLight3D` and `SpotLight3D` shadow casting. Keep shadows disabled on small accent lights.
   - Set conservative shadow maximum distances.
4. **Viewmodel Dual-Camera / Overlay Setup**:
   - To prevent the player's weapon and arms from clipping through walls when walking up to surfaces:
     - Use a separate Viewmodel SubViewport or dual-camera rendering setup with a dedicated culling mask.
     - Keep the viewmodel FOV separate from world FOV (industry standard for competitive FPS feel).
5. **Decal Management**:
   - Hard cap decal count (e.g., maximum 128 active bullet holes).
   - Manage decals via a circular FIFO buffer. When the limit is reached, fade out and recycle the oldest decal.

---

## 5. Testing Methodology in Game Development

Testing in game development differs from standard web/backend software because it spans deterministic logic, physics simulations, and visual validation.

### 5.1 The Three-Tier Testing Pyramid

```
       / \
      /   \      Tier 3: World Gyms (Interactive Stress & Feel Validation)
     /     \     - Movement gym, ballistics test range, AI benchmark arena
    /-------\
   /         \   Tier 2: Headless Godot Integration Tests (Engine/Nodes)
  /           \  - Scene loading, collision trigger verification, pool checkout
 /-------------\
/               \ Tier 1: Pure C# Unit Tests (Deterministic Logic)
----------------- - Weapon recoil curves, damage formulas, inventory math
```

### 5.2 Tier 1: Pure C# Unit Tests (`tests/unit/`)
- **What to test**: Logic that has zero dependency on the Godot engine runtime.
  - Health/Shield math (damage reduction, armor formulas).
  - Weapon balance (spread increase per shot, recoil recovery curves, ammo consumption).
  - State machine transitions (valid transitions: `Idle` -> `Sprint` -> `Slide`; invalid: `Dead` -> `Jump`).
  - Object pool borrowing, returning, capacity doubling.
- **Execution**: Can be run via standard `dotnet test` or test runner in milliseconds without launching the Godot engine.

### 5.3 Tier 2: Headless Godot Integration Tests (`tests/integration/`)
- **What to test**: Interactions requiring Godot's node hierarchy or physics server.
  - Spawning a projectile and verifying it damages a target's `HurtboxComponent` on hit.
  - Verifying `HealthComponent` emits `OnDeath` event when lethal damage is received.
  - Ensuring `NodePool<T>` properly activates/deactivates nodes in the scene tree.
- **Execution**: Run with Godot CLI in headless mode:
  ```bash
  godot --headless --run-tests
  # or via GdUnit4 C# test framework
  ```

### 5.4 Tier 3: Interactive World Gyms (`src/levels/world_gym/`)
Dedicated test arenas used by developers to tune feel, physics, and verify performance:
1. `movement_gym.tscn`: Slopes, stairs, 45-degree ramps, low ceilings, ladders, mantling ledges, friction testing.
2. `ballistics_gym.tscn`: Static target dummies at 10m, 25m, 50m; armored targets, destructible glass, penetration walls.
3. `ai_gym.tscn`: Cover nodes, navigation mesh pathfinding, line-of-sight checks, state transitions.
4. `benchmark_stress.tscn`: Spawns 100 AI agents and fires 1,000 projectiles simultaneously while monitoring FPS, frame times, and draw calls.

### 5.5 Agent Quality Checklist Before Completing Any Work
Whenever an agent creates or modifies a feature:
- [ ] Code builds cleanly with zero compiler warnings or errors (`dotnet build`).
- [ ] No allocations added to `_Process` or `_PhysicsProcess`.
- [ ] Nodes are composed cleanly with appropriate components.
- [ ] Collision layers match the Layer Blueprint table strictly.
- [ ] Relevant unit tests or gym test scenes are provided or updated.
- [ ] **NO Git commits or staging performed.**

---

## 6. Architecture & Coding Standards

### 6.1 Entity-Component Model (Node Composition)
Favor composition over deep class inheritance hierarchies:
- **`HealthComponent`**: Manages current/max HP, shields, invulnerability frames, death signals.
- **`HurtboxComponent` (`Area3D`)**: Receives incoming damage, references bone multipliers, forwards damage to parent `HealthComponent`.
- **`HitboxComponent` (`Area3D`)**: Represents an active attacking volume (melee strikes, explosions).
- **`InteractionComponent`**: Handles proximity detection and activation prompts for interactable objects.
- **`MovementController`**: Handles velocity math, acceleration, friction, jump, slide, crouch.

### 6.2 C# Conventions & Godot 4 Idioms
- **Class Declarations**: Use `partial` class for all Godot node scripts (`public partial class PlayerController : CharacterBody3D`).
- **Registration**: Annotate custom resources and reusable nodes with `[GlobalClass]` so they appear cleanly in the Godot Inspector.
- **Node References**:
  - Prefer `[Export]` for Inspector-assigned node dependencies.
  - For internal sub-nodes, cache references in `_Ready()` using `GetNode<T>()`.
  - Never call `GetNode<T>()` in `_Process()` or `_PhysicsProcess()`.
- **Events & Decoupling**:
  - Use strongly-typed C# events (`public event Action<DamageInfo> OnDamaged;`) or the global `EventBus` for system-level communication.
  - Avoid tight bidirectional dependencies between UI and entities (UI subscribes to events, entities never hold UI references).
- **Disposal**: Always unsubscribe from C# events in `_ExitTree()` to prevent memory leaks from dangling delegates.

### 6.3 Network Architecture & RPC Standards
- **Server Authority**: The server is the single source of truth for health, ammo, hit validation, scores, and round state.
- **RPC Guidelines**:
  - Use `[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]` for high-frequency inputs and position sync.
  - Use `[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]` for critical state transitions (damage taken, deaths, round start/end).
  - Never allow clients to RPC "I killed player X" or "I dealt 50 damage". Clients RPC "I fired towards direction D at tick T"; the server simulates the raycast and validates.
- **Client Prediction**: Local player runs instant movement simulation; remote players use interpolated snapshots via `MultiplayerSynchronizer` or lerped transform buffers.

---

## 7. Summary Quick-Reference

| When you are creating... | Put it in... | Notes |
|---|---|---|
| Player controller, camera, viewmodel scripts | `src/entities/player/` | Separate movement, camera, and viewmodel logic |
| Reusable components (Health, Hurtbox, etc.) | `src/common/components/` | Modular nodes attached to entities |
| Gun scripts, recoil, ammo data definitions | `src/weapons/` | Base classes in `base/`, data definitions in `data/` |
| Weapon stats asset (Damage, RPM, Spread) | `data/resources/weapons/` | C# `[GlobalClass]` custom `Resource` (.tres) |
| 3D models, textures, animations | `assets/models/`, `assets/textures/` | Organized by entity/type |
| Sound effects, footsteps, gunfire audio | `assets/audio/sfx/` | Grouped by category (.wav, .ogg) |
| Test scenes, gyms, stress benchmarks | `src/levels/world_gym/` | Used to validate mechanics and framerates |
| Unit tests for pure C# calculations | `tests/unit/` | Fast, deterministic tests |

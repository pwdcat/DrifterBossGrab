# DrifterBossGrab

Allows Drifter to grab and bag anything

## Risk of Options Compatibility

This mod is compatible with [Risk of Options](https://thunderstore.io/c/riskofrain2/p/Rune580/Risk_Of_Options/) for in-game configuration.

## Configuration

Configurable options that can be adjusted in the config file (`BepInEx/config/pwdcat.DrifterBossGrab.cfg`) or via Risk of Options.

### General
- **EnableBossGrabbing** (true/false): Enable grabbing of boss enemies.
- **EnableNPCGrabbing** (true/false): Enable grabbing of NPCs with ungrabbable flag.
- **EnableEnvironmentGrabbing** (true/false): Enable grabbing of environment objects like teleporters, chests, shrines.
- **EnableLockedObjectGrabbing** (true/false): Enable grabbing of locked objects.
- **EnableProjectileGrabbing** (true/false): Enable grabbing of projectiles.
- **ProjectileGrabbingSurvivorOnly** (true/false): Restrict projectile grabbing to only those fired by survivor players.
- **Blacklist**: Comma-separated list of body and projectile names to never grab. Automatically handles (Clone).
- **RecoveryObjectBlacklist**: Comma-separated list of object names to never recover from the abyss.
- **GrabbableComponentTypes**: Comma-separated list of component type names that make objects grabbable (e.g., PurchaseInteraction).
- **GrabbableKeywordBlacklist**: Comma-separated list of keywords that make objects NOT grabbable if found in their name (e.g., Master).
- **EnableDebugLogs**: Enable debug logging.
- **EnableComponentAnalysisLogs**: Enable performance-intensive scanning of objects in the scene to log component types.

### Skill
- **SearchRangeMultiplier**: Multiplier for Drifter's repossess search range.
- **ForwardVelocityMultiplier**: Multiplier for Drifter's repossess forward velocity.
- **UpwardVelocityMultiplier**: Multiplier for Drifter's repossess upward velocity.
- **BreakoutTimeMultiplier**: Multiplier for how long bagged enemies take to break out.
- **MaxSmacks** (1-100): Maximum number of hits before bagged enemies break out.
- **MassMultiplier**: Multiplier for the mass of bagged objects.

### Bottomless Bag
- **EnableBottomlessBag** (true/false): Allows the scroll wheel to cycle through stored passengers. Bag capacity scales with the number of repossesses.
- **BaseCapacity** (0-100): Base capacity for bottomless bag, added to utility max stocks.
- **EnableStockRefreshClamping** (true/false): When enabled, Repossess stock refresh is clamped to max stocks minus number of bagged items.
- **CycleCooldown**: Cooldown between passenger cycles in seconds.
- **EnableMouseWheelScrolling** (true/false): Enable mouse wheel scrolling for cycling passengers.
- **InverseMouseWheelScrolling** (true/false): Invert the mouse wheel scrolling direction.
- **ScrollUpKeybind**: Keybind to scroll up through passengers.
- **ScrollDownKeybind**: Keybind to scroll down through passengers.
- **AutoPromoteMainSeat** (true/false): Automatically promote the next object in the bag to the main seat when the current main object is removed.

### Persistence
- **EnableObjectPersistence** (true/false): Enable persistence of grabbed objects across stage transitions.
- **EnableAutoGrab** (true/false): Automatically re-grab persisted objects on Drifter respawn.
- **PersistBaggedBosses** (true/false): Allow persistence of bagged boss enemies.
- **PersistBaggedNPCs** (true/false): Allow persistence of bagged NPCs.
- **PersistBaggedEnvironmentObjects** (true/false): Allow persistence of bagged environment objects.
- **PersistenceBlacklist**: Comma-separated list of object names to never persist.

### Hud
- **CarouselSpacing**: Vertical spacing for carousel items.
- **CarouselCenterOffset (X/Y)**: Positional offsets for the center carousel item.
- **CarouselSideOffset (X/Y)**: Positional offsets for the side carousel items.
- **CarouselSideScale**: Scale for side carousel items.
- **CarouselSideOpacity**: Opacity for side carousel items.
- **CarouselAnimationDuration**: Duration of carousel transition animations.
- **BagUIShowIcon**: Show icon in additional Bag UI elements.
- **BagUIShowWeight**: Show weight indicator in additional Bag UI elements.
- **BagUIShowName**: Show name in additional Bag UI elements.
- **BagUIShowHealthBar**: Show health bar in additional Bag UI elements.
- **UseNewWeightIcon**: Use the new custom weight icon instead of the original.
- **ShowWeightText**: Show weight multiplier text on the weight icon.
- **ScaleWeightColor**: Scale the weight icon color based on mass.

## Credits

- **plontII**: For the original idea for the Bottomless Bag feature.
- **Matsan**: For the weight icon design and future balancing suggestions.
# DrifterBossGrab

Allows Drifter to grab and bag any object or NPC.

## Risk of Options Compatibility

This mod is compatible with [Risk of Options](https://thunderstore.io/c/riskofrain2/p/Rune580/Risk_Of_Options/) for in-game configuration.

## Configuration

Configurable options that can be adjusted in the config file (`BepInEx/config/pwdcat.DrifterBossGrab.cfg`) or via Risk of Options.

### Grabbing Toggles
- **EnableBossGrabbing** (true/false): Enable grabbing of boss enemies.
- **EnableNPCGrabbing** (true/false): Enable grabbing of NPCs with ungrabbable flag.
- **EnableEnvironmentGrabbing** (true/false): Enable grabbing of environment objects (teleporters, chests, shrines). Refer to [GrabbableComponentTypes](#filtering) for more control.
- **EnableLockedObjectGrabbing** (true/false): Enable grabbing of locked objects during the teleporter event.
- **EnableProjectileGrabbing** (true/false): Enable grabbing of projectiles.
- **ProjectileGrabbingSurvivorOnly** (true/false): Restrict projectile grabbing to only those fired by survivor players.
- **Bottomless Bag** (true/false): Bagged objects are now stored in a "bottomless" bag scaled off of how many repossesses you have. Use your scroll wheel or keybinds to switch between objects.
- **BaseCapacity** (0-100): Base capacity for bottomless bag, added to utility max stocks.
- **EnableStockRefreshClamping** (true/false): When enabled, Repossess stock refresh is clamped to max stocks minus number of bagged items.
- **EnableMouseWheelScrolling** (true/false): Enable mouse wheel scrolling for cycling passengers.
- **ScrollUpKeybind**: Keybind to scroll up through passengers.
- **ScrollDownKeybind**: Keybind to scroll down through passengers.

### Persistence
- **EnableObjectPersistence** (true/false): Enable persistence of grabbed objects across stage transitions.
- **EnableAutoGrab** (true/false): Automatically re-grab persisted objects when Drifter respawns in a new stage.
- **PersistBaggedBosses** (true/false): Allow persistence of bagged boss enemies.
- **PersistBaggedNPCs** (true/false): Allow persistence of bagged NPCs.
- **PersistBaggedEnvironmentObjects** (true/false): Allow persistence of bagged environment objects.
- **PersistenceBlacklist** (string): Comma-separated list of object names to never persist.

### Skill
- **SearchRangeMultiplier**: Multiplier for Drifter's repossess search range.
- **ForwardVelocityMultiplier**: Multiplier for Drifter's repossess forward velocity.
- **UpwardVelocityMultiplier**: Multiplier for Drifter's repossess upward velocity.
- **BreakoutTimeMultiplier**: Multiplier for how long bagged enemies take to break out.
- **MaxSmacks** (1-100): Maximum number of hits before bagged enemies break out.
- **MassMultiplier**: Multiplier for the mass of bagged objects.

### Filtering
- **Blacklist** (string): Comma-separated list of body and projectile names to never grab.
- **GrabbableComponentTypes** (string): Comma-separated list of component type names that make objects grabbable.
- **GrabbableKeywordBlacklist** (string): Comma-separated list of keywords that make objects NOT grabbable if found in their name.
- **RecoveryObjectBlacklist** (string): Comma-separated list of object names to never recover from abyss falls.

### Debug
- **EnableDebugLogs** (true/false): Enable debug logging.
- **EnableComponentAnalysisLogs** (true/false): Enable scanning of all objects in the current scene to log component types (performance-intensive, for debugging only).
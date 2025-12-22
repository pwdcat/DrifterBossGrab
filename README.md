# DrifterBossGrab

Allows Drifter to grab and bag any object or NPC.

## Risk of Options Compatibility

This mod is compatible with [Risk of Options](https://thunderstore.io/c/riskofrain2/p/Rune580/Risk_Of_Options/) for in-game configuration.

## Configuration

Configurable options that can be adjusted in the config file (`BepInEx/config/com.DrifterBossGrab.DrifterBossGrab.cfg`) or via Risk of Options:

### Repossess Section
- **SearchRangeMultiplier**: Multiplier for Drifter's repossess search range.
- **ForwardVelocityMultiplier**: Multiplier for Drifter's repossess forward velocity.
- **UpwardVelocityMultiplier**: Multiplier for Drifter's repossess upward velocity.

### Bag Section
- **BreakoutTimeMultiplier**: Multiplier for how long bagged enemies take to break out.
- **MaxSmacks** (1-100): Maximum number of hits before bagged enemies break out.
- **MassMultiplier**: Multiplier for the mass of bagged objects.

### General Section
- **EnableBossGrabbing** (true/false): Enable grabbing of boss enemies.
- **EnableNPCGrabbing** (true/false): Enable grabbing of NPCs with ungrabbable flag.
- **EnableEnvironmentGrabbing** (true/false): Enable grabbing of environment objects.
- **EnableLockedObjectGrabbing** (true/false): Enable grabbing of locked objects.
- **EnableDebugLogs** (true/false): Enable debug logging.
- **EnableComponentAnalysisLogs** (true/false): Enable scanning of all objects in the current scene to log component types.
- **BodyBlacklist** (string): Comma-separated list of body names to never grab.
- **GrabbableComponentTypes** (string): Comma-separated list of component type names that make objects grabbable. (Recommend upping the search range and use the ping to find where to grab for huge objects)
- **GrabbableKeywordBlacklist** (string): Comma-separated list of keywords that make objects NOT grabbable if found in their name.
- **RecoveryObjectBlacklist** (string): Comma-separated list of object names to never recover from abyss falls. Leave empty to recover all objects.

### Persistence Section
- **EnableObjectPersistence** (true/false): Enable persistence of grabbed objects across stage transitions.
- **EnableAutoGrab** (true/false): Automatically re-grab persisted objects when Drifter respawns in a new stage.
- **PersistBaggedBosses** (true/false): Allow persistence of bagged boss enemies.
- **PersistBaggedNPCs** (true/false): Allow persistence of bagged NPCs.
- **PersistBaggedEnvironmentObjects** (true/false): Allow persistence of bagged environment objects.
- **PersistenceBlacklist** (string): Comma-separated list of object names to never persist. Leave empty to allow all objects.
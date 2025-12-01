# DrifterBossGrab

Allows Drifter to grab and bag boss enemies, NPCs, and environment objects

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
- **MassMultiplier**: Multiplier for the mass of bagged objects (affects throwing physics).

### General Section
- **EnableBossGrabbing** (true/false): Enable grabbing of boss enemies.
- **EnableNPCGrabbing** (true/false): Enable grabbing of NPCs with ungrabbable flag.
- **EnableEnvironmentGrabbing** (true/false): Enable grabbing of environment objects like teleporters and portals.
- **EnableEnvironmentInvisibility** (true/false): Make grabbed environment objects invisible while in the bag.
- **EnableEnvironmentInteractionDisable** (true/false): Disable interactions for grabbed environment objects while in the bag.
- **EnableUprightRecovery** (true/false): Reset rotation of recovered thrown objects to upright position.
- **EnableDebugLogs** (true/false): Enable debug logging.
- **BodyBlacklist** (string): Comma-separated list of body names to never grab. Defaults to "MinePodBody,HeaterPodBodyNoRespawn,GenericPickup" for safety.

### Recovery Section
- **RecoveryObjectBlacklist** (string): Comma-separated list of object names to never recover from abyss falls. Leave empty to recover all objects.
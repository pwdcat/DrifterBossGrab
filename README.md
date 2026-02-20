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
- **ProjectileGrabbingMode** (None/SurvivorOnly/AllProjectiles): Mode for projectile grabbing.
- **Blacklist**: Comma-separated list of body and projectile names to never grab. Automatically handles (Clone).
- **RecoveryObjectBlacklist**: Comma-separated list of object names to never recover from the abyss.
- **GrabbableComponentTypes**: Comma-separated list of component type names that make objects grabbable (e.g., PurchaseInteraction).
- **GrabbableKeywordBlacklist**: Comma-separated list of keywords that make objects NOT grabbable if found in their name (e.g., Master).
- **EnableDebugLogs** (true/false): Enable debug logging.
- **EnableComponentAnalysisLogs** (true/false): Enable scan of all objects in the current scene to log component types.
- **EnableConfigSync** (true/false): Enable synchronization of configuration settings from host to new clients.

### Skill
- **SearchRangeMultiplier**: Multiplier for Drifter's repossess search range.
- **ForwardVelocityMultiplier**: Multiplier for Drifter's repossess forward velocity.
- **UpwardVelocityMultiplier**: Multiplier for Drifter's repossess upward velocity.
- **BreakoutTimeMultiplier**: Multiplier for how long bagged enemies take to break out.
- **MaxSmacks** (1-100): Maximum number of hits before bagged enemies break out.
- **MassMultiplier**: Multiplier for the mass of bagged objects.

### Bottomless Bag
- **EnableBottomlessBag** (true/false): Allows the scroll wheel to cycle through stored passengers. Bag capacity scales with the number of repossesses.
- **BaseCapacity**: Base capacity for bottomless bag, added to utility max stocks.
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
- **AutoGrabDelay**: Delay before auto-grabbing persisted objects on stage start (seconds).

### Hud
- **EnableCarouselHUD** (true/false): Enable the custom Carousel HUD for Drifter's bag. (Auto-enabled with Bottomless Bag)
- **CarouselSpacing**: Vertical spacing for carousel items.
- **CarouselCenterOffsetX**: Horizontal offset for center carousel item.
- **CarouselCenterOffsetY**: Vertical offset for center carousel item.
- **CarouselSideOffsetX**: Horizontal offset for side carousel items.
- **CarouselSideOffsetY**: Vertical offset for side carousel items.
- **CarouselSideScale**: Scale for side carousel items.
- **CarouselSideOpacity**: Opacity for side carousel items.
- **CarouselAnimationDuration**: Duration of carousel transition animations.
- **BagUIShowIcon**: Show icon in additional Bag UI elements.
- **BagUIShowWeight**: Show weight indicator in additional Bag UI elements.
- **BagUIShowName**: Show name in additional Bag UI elements.
- **BagUIShowHealthBar**: Show health bar in additional Bag UI elements.
- **UseNewWeightIcon**: Use the new custom weight icon instead of the original.
- **WeightDisplayMode** (None/Multiplier/Pounds/KiloGrams): Mode for displaying weight.
- **ScaleWeightColor**: Scale the weight icon color based on mass.
- **EnableDamagePreview**: Show a damage preview overlay on bagged object health bars.
- **DamagePreviewColor**: Color for the damage preview overlay.
- **EnableMassCapacityUI**: Enable the Mass Capacity UI for displaying bag capacity status.
- **MassCapacityUIPositionX**: Horizontal position offset for the Mass Capacity UI.
- **MassCapacityUIPositionY**: Vertical position offset for the Mass Capacity UI.
- **MassCapacityUIScale**: Scale multiplier for the Mass Capacity UI.

### Balance
- **EnableBalance** (true/false): Enable balance features (capacity scaling, elite mass bonus, overencumbrance).
- **EnableAoESlamDamage** (true/false): When enabled, slam/bluntforce actions damage every object in the bag (requires 'All' calculation mode).
- **AoEDamageDistribution** (Full/Split): Mode for AoE damage distribution.
- **CapacityScalingMode** (IncreaseCapacity/HalveMass): Mode for capacity scaling based on utility stocks.
- **CapacityScalingType** (Linear/Exponential): Type of capacity scaling.
- **CapacityScalingBonusPerCapacity**: Bonus mass capacity per utility stock.
- **EliteMassBonusEnabled** (true/false): Enable elite mass bonus.
- **EliteMassBonusPercent**: Percentage mass bonus for elites.
- **EnableOverencumbrance** (true/false): Enable overencumbrance penalties when exceeding mass capacity.
- **OverencumbranceMaxPercent**: Maximum overencumbrance percentage.
- **UncapCapacity** (true/false): When enabled, storage is practically infinite (slot count ignored).
- **ToggleMassCapacity** (true/false): When enabled, both slot and mass capacity are enforced.
- **StateCalculationModeEnabled** (true/false): Enable choosing state calculation mode.
- **StateCalculationMode** (Current/All): Mode for calculating bagged object state (affects stats/mass).
- **AllModeMassMultiplier**: Multiplier for mass calculation in All mode.
- **UncapBagScale** (true/false): When enabled, bag visual size is not capped.
- **UncapMass** (true/false): When enabled, mass cap of 700 is removed.
- **MinMovespeedPenalty**: Minimum movement speed penalty (as a percentage of base movement speed) when bag is empty.
- **MaxMovespeedPenalty**: Maximum movement speed penalty (as a percentage of base movement speed) when bag is full.
- **FinalMovespeedPenaltyLimit**: Final limit for movement speed penalty after multipliers are applied.

## Credits

- **plontII**: For the idea for the Bottomless Bag and clamp feature.
- **Matsan**: For the weight icon and balancing suggestions.
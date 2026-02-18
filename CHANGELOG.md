# Changelog

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.7.0]

### Added
- Balance features:
    - ToggleMassCapacity
    - Capacity Scaling System for mass:
        - IncreaseCapacity
        - HalveMass
    - Elite Mass Bonus
    - Overencumbrance System
    - State Calculation System:
        - Current
        - All
    - AoE Slam Damage: Slam/bluntforce damage every object in the bag
        - Full
        - Split
    - UncapCapacity

- HUD features:
    - Weight Display for Pounds and Kilograms (still need some tweaking)
    - Capacity UI

### Fixed
- Clamping no longer fully refresh all stocks
- ModelStatePerserver applying when cycling for the client
- Fix lossyscale restore being applied twice when reparenting
- Tracking when an enemy teleports out of your bag
- Bottomlessbag not spawning junk after destroying an object
- Projectile Grabbing overriding values, like for junk cube
- Issue when grabbing other drifters with bottomless (not a full fix but it won't soft lock you this time)

### Changed
- Refactoring and optimizations
- Split UncapBagScale to UpcapBagScale (increase bag size based on mass, probably would want uncapmass on) and UncapMass (removes 700 mass cap)

## [1.6.10]

### Fixed
- ModelPerserver adding itself despite my changes (while hosting)
- ModelLocator.autoUpdateModelTransform being applied while persistence is off
- Initial grab will correctly update the UI
- Actually fix autograb at capacity = 1
- Fix client persistence
- Fix auto grab, now works for other clients
- Fix bug with persistence, can now persist an object that is currently in your bag and can persist more than once
- Update Projectile Config, cleaner with a dropdown

### Note
- If you noticed weird UI issues when grabbing, it means you're desynced with the host - Most likely the host has bottomless bag on. You can turn it on midgame. Config syncing still needs more testing so don't expect it to be flawless.
- You need Carousel hud on for BottomlessBag (might force it on in the future)

## [1.6.9]

### Fixed
- Duplicate UI overlapping
- Remove restriction for Autograb at capacity = 1
- Desync when grabbing objects mid-air
- Mass sync for clients
- Tighter Grabbing container, no modelperserver
- Cycle wrap around
- Bug involving needing to grab an additional object to scroll

### Note
- Might be a little bloated was in a middle of refactoring BottomlessBag (no changes yet)

## [1.6.8]

### Added
- Containers for each feature, shouldn't interfere with each other for future updates
- So Persistence, Bottomless Bag, and Grabbing will be separate

## [1.6.7]

### Added
- UncapBagScale configuration
- AutoGrabDelay configuration

### Fixed
- Multiplayer:
    - Host-authoritative persistence to prevent NPC duplication in multiplayer
    - Restricted autograb to host player as a temporary stability measure for clients
    - Improved seat assignment and collision handling for additional passengers
    - Fixed impact visibility desynchronization
- Persistence:
    - Fixed NullReferenceException in StopCoroutines when leaving or closing the game
    - Safer logging to prevent NREs
    - UI updates now correctly reflect object destruction
- General:
    - Fixed recovery
    - Resolved teleportation bug for objects with pre-existing vehicle seats (Solus Wing)

## [1.6.6]
### Fixed
- Improved skill override cleanup
- NullReferenceExceptions in logging when Unity objects are destroyed
- Mass recalculation
- Persistent slow movement when bagged objects are destroyed

## [1.6.5]
### Fixed
- Skill overrides persisting after bagged objects are destroyed in the bag/scene (sorry Bread)

## [1.6.4]
### Added
- UI for Bottomless Bag
- UI animations for bag transitions
- Risk of Options setting for EnableStockRefreshClamping

### Fixed
- General multiplayer synchronization and setup

### Note
- Autograb is still currently broken
- Persistence should be fine, it'll spawn on the host but should be synced
- Bottomless Bag has a bug where you get 1 extra capacity, don't feel like fixing it so enjoy the extra capacity

## [1.6.3]

### Fixed
- Boss grabbing now works independently of NPC grabbing (needed to turn on both for it to work)

## [1.6.2]

### Added
- EnableStockRefreshClamping

### Fixed
- Multiplayer fix UI and overrides for client
- Persistence multiplayer

### Note
- Main focus for the next couple of updates is going to be for multiplayer
- BottomlessBag is still broken for multiplayer (only the host can get it working)
- Autograb for persistence doesn't work for anyone but the host (will fix it later)

## [1.6.1]

### Added
- Base capacity option for bottomless bag (adds to utility max stocks)
- Toggle for enabling mouse wheel scrolling
- Keybinds for manual scrolling

### Fixed
- Overlapping BaggedObject data
- Blunt force being overrided when there's no object in main vehicle

## [1.6.0]

### Added
- Bottomless Bag

## [1.5.2]

### Fixed
- Objects with ModelStatePreserver resetting to original location when thrown - now thrown objects stay at their landed position

## [1.5.1]

### Fixed
- Support for persistence for NPCs and bosses, you can persist Mithrix
- Certain objects not behaving correctly after thrown
- Persistence with [TossedInTransit](https://thunderstore.io/package/swuff-star/TossedInTransit/)

### Added
- Some icons

### Removed
- Some redundancy

## [1.5.0]

### Added
- Projectile grabbing functionality

### Changed
- Reorganized Risk of Options settings, sorry you might need check your old config to port it over

## [1.4.6]

### Removed
- `OnlyPersistCurrentlyBagged` configuration option - persistence now only applies to objects currently in the bag
- Thrown object persistence feature entirely
- Teleporter persistence window logic

### Changed
- Simplified persistence logic to always only capture bagged objects
- Persistence now works with objects with dither models (like common chests and shrines)

### Fixed
- Multiplayer synchronization for removing objects from persistence on impact

## [1.4.5]

### Fixed 
- Fixed EnableBossGrabbing and EnableNPCGrabbing not working correctly

## [1.4.4]

### Added
- Configurable toggle to enable grabbing of locked objects

## [1.4.3]

### Changed
- Added "Controller" to default grabbable keyword blacklist
- Replacing "EntityStateMachine" with "TeleporterInteraction" from GrabbableComponentTypes, seems too problematic (just readded it if you want it)
- Added "MultiShopTerminal,MultiShopLargeTerminal" to default body blacklist

### Fixed
- Bug when there's two teleporters in a stage, still buggy
- Bug when throwing the junkcube
- General refactor, removed some doodoo code

## [1.4.2]

### Added
- Support for grabbing Captain supply drops

## [1.4.1]

### Added
- Mass and durability now scale based on object collider volume (0.5x to 5x range)

### Fixed
- Teleporter interaction bug
- Grabbed object icons
 
### Changed
- Removed numeric suffixes like (1)

## [1.4.0]

### Added
- Dynamic SpecialObjectAttributes addition to all specified objects on spawn

### Changed
- Completely replaced the 500-object caching limit with SpecialObjectAttributes-based system
- Search logic now uses SpecialObjectAttributes instead of cached IInteractable lists

### Fixed
- Recovery for certain maps
- Multi shops causing NRE, when broken

## [1.3.2]

### Added
- Mutliplayer Support for Persistence

## [1.3.1]

### Added
- Autograb, now implemented

### Fixed
- Teleporter persistence

## [1.3.0]

### Added
- Bagged objects now survive stage transitions and can be manually re-grabbed in the new stage (no multiplayer support yet)

### Fixed
- Recovery system bug in stages without OutOfBounds zones causing immediate recovery

### Changed
- All thrown objects now have upright rotation by default

## [1.2.2]

### Added
- Projectile recovery system for objects thrown out of bounds
- Configurable upright recovery option to reset rotation
- Recovery object blacklist to prevent certain objects from being recovered

### Changed
- Code refactoring

### Fixed
- Multiplayer bugs, enviornmental objects weren't correctly synced

## [1.2.1]

### Fixed
- Bug when grabbing object mid air that added two GrabbedObjectStates, causing visibility and collision issues
- DisableMovementColliders now includes all colliders and works with Spex
- Possible memory leak from event handlers
- Legendary chest interaction

### Added
- Max cache size

### Changed
- Replaced static shared dictionaries to prevent buildup over time

## [1.2.0]

### Fixed
- Environment objects now restore states on projectile impact instead of immediately after throwing
- Better performance with cached config values, optimized blacklist checking, improved component caching
- Replaced timer-based interactable caching with hooks
- Reduced garbage collection
- Preserved original behavior for blacklisted objects in SpecialObjectAttributes
- Improved DisableMovementColliders to handle both CollideWithCharacterHullOnly and World layers

### Added
- Interactables are added/removed from cache as they spawn/destroy
- Cache automatically refreshes when players join to catch late-loaded objects
- SpecialObjectAttributes patches now apply per entity type
- Configurable mass multiplier for bagged objects

### Changed
- Default config values
- Cleaner code for disabling movement-related colliders

## [1.1.3]

### Fixed
- Fixed hacking beacon not working correctly when grabbed

### Added
- Configurable environment invisibility toggle
- Configurable environment interaction disable toggle
- Configurable forward velocity multiplier
- Configurable upward velocity multiplier

## [1.1.2]

### Fixed
- Fixed StandableSurface not being disabled for objects with multiple StandableSurface (SolusAmalgamatorBody)
- Fixed error when bagging objects with StandableSurface (like junkcube), it spams junk all over my face

### Note
- 1.1.1 is the same as 1.1.0, I'm an idiot

## [1.1.0]

### Added
- Risk of Options Compatibility
- Configurable blacklist for enemies and environment objects (defaults to MinePodBody,HeaterPodBodyNoRespawn)

### Changed
- Updated BepInEx dependency, my bad

## [1.0.8]

### Fixed
- Fixed movement bug when grabbing flying bosses like Vagrant and MegaConstruct

## [1.0.7]

### Fixed
- Printers and other interactable objects now remain interactable after being thrown
- Only disable physics colliders during grab, preserve trigger colliders for interaction

## [1.0.6]

### Added
- Support for grabbing NPCs with ungrabbable flag, like Newt
- Configurable NPC grabbing toggle
- Configurable MaxSmacks setting to control how many hits before bagged enemies break out

### Removed
-  Configurable max mass

## [1.0.5]

### Added
- Support for grabbing environment objects, there's a lot (I've done zero testing so GL)
- Configurable environment grabbing toggle
- Fixed collision
- Needed to push this ASAP, it's funny af

### Note
- 1.0.4 was skipped (same as 1.0.3, got a little too excited)

## [1.0.3]

### Added
- Support for grabbing SolusWing
- Configurable max mass for bagged objects

## [1.0.2]

### Added
- Configurable search range multiplier
- Configurable breakout time multiplier
- Configurable boss grabbing toggle

## [1.0.1]

### Fixed
- Corrected namespace, remove some template crap
- Removed Breakout Time, was mainly for testing

## [1.0.0]

### Added
- Boss grabbing functionality
- Fixed momentum issues after grabbing
- Support for all boss enemies (hopefully), only tested stage 1 bosses
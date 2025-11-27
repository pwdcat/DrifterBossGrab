# Changelog

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.2.0] - 2025-11-26

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

### Changed
- Default config values
- Cleaner code for disabling movement-related colliders

## [1.1.3] - 2025-11-26

### Fixed
- Fixed hacking beacon not working correctly when grabbed

### Added
- Configurable environment invisibility toggle
- Configurable environment interaction disable toggle
- Configurable forward velocity multiplier
- Configurable upward velocity multiplier

## [1.1.2] - 2025-11-25

### Fixed
- Fixed StandableSurface not being disabled for objects with multiple StandableSurface (SolusAmalgamatorBody)
- Fixed error when bagging objects with StandableSurface (like junkcube), it spams junk all over my face

### Note
- 1.1.1 is the same as 1.1.0, I'm an idiot

## [1.1.0] - 2025-11-24

### Added
- Risk of Options Compatibility
- Configurable blacklist for enemies and environment objects (defaults to MinePodBody,HeaterPodBodyNoRespawn)

### Changed
- Updated BepInEx dependency, my bad

## [1.0.8] - 2025-11-24

### Fixed
- Fixed movement bug when grabbing flying bosses like Vagrant and MegaConstruct

## [1.0.7] - 2025-11-23

### Fixed
- Printers and other interactable objects now remain interactable after being thrown
- Only disable physics colliders during grab, preserve trigger colliders for interaction

## [1.0.6] - 2025-11-23

### Added
- Support for grabbing NPCs with ungrabbable flag, like Newt
- Configurable NPC grabbing toggle
- Configurable MaxSmacks setting to control how many hits before bagged enemies break out

### Removed
-  Configurable max mass

## [1.0.5] - 2025-11-23

### Added
- Support for grabbing environment objects, there's a lot (I've done zero testing so GL)
- Configurable environment grabbing toggle
- Fixed collision
- Needed to push this ASAP, it's funny af

### Note
- 1.0.4 was skipped (same as 1.0.3, got a little too excited)

## [1.0.3] - 2025-11-23

### Added
- Support for grabbing SolusWing
- Configurable max mass for bagged objects

## [1.0.2] - 2025-11-22

### Added
- Configurable search range multiplier
- Configurable breakout time multiplier
- Configurable boss grabbing toggle

## [1.0.1] - 2025-11-22

### Fixed
- Corrected namespace, remove some template crap
- Removed Breakout Time, was mainly for testing

## [1.0.0] - 2025-11-22

### Added
- Boss grabbing functionality
- Fixed momentum issues after grabbing
- Support for all boss enemies (hopefully), only tested stage 1 bosses
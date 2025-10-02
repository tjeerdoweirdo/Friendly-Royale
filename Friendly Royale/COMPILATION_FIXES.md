# Compilation Error Fixes

## Current Status
The networking scripts I created require Unity Netcode for GameObjects to be properly installed and compiled. The current errors are due to missing assembly references.

## Immediate Fixes Applied

### 1. NetworkCardSpawner.cs - Fixed Spell Reference
- Changed `card.spellPrefab` to `card.spellAsset` (correct property name)
- Updated spell casting to use `card.spellAsset.Cast(position, faction)` directly
- Removed invalid `Spells` class reference (should be `Spell`)

### 2. TowerHealthBar Method Names
- `SetTower()` method doesn't exist - should use `AttachTo()`
- `UpdateHealthBar()` should be `UpdateHealth()`

### 3. UnitHealth Event Subscription  
- `OnDeath` event doesn't exist - should use `onDie` UnityEvent
- Fixed subscription: `unitHealth.onDie.AddListener(OnUnitDeath)`

### 4. Projectile Configuration
- `SetTarget()` method doesn't exist - should use `Configure()`
- Updated to: `projectile.Configure(damage, ownerTag, target)`

### 5. Faction Enum Compatibility
- NetworkUnit.Faction vs Unit.Faction type mismatch
- Fixed with casting: `(Unit.Faction)(int)networkUnit.faction`

## To Resolve Networking Errors

The remaining compilation errors are due to Unity Netcode for GameObjects not being fully installed. To fix:

1. **Close Unity**
2. **Reopen the project** - Unity will detect the new package in manifest.json
3. **Wait for compilation** - The Netcode package will be downloaded and compiled
4. **If errors persist**, go to Window > Package Manager > Unity Registry > search for "Netcode for GameObjects" and install manually

## Alternative: Use Original Scripts
If you prefer to stick with single-player for now, continue using your original scripts:
- `CardSpawner.cs` instead of `NetworkCardSpawner.cs`  
- `GameManager.cs` (your current version)
- `Unit.cs`, `Tower.cs`, `UnitHealth.cs` (original versions)

The networking scripts are ready to use once Unity Netcode is properly installed and compiled.
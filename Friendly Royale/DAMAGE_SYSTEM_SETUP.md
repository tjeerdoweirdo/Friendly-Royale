# Friendly Royale - Damage System Setup Tutorial

## Overview
This tutorial will help you set up the damage system for your Friendly Royale game. The system includes unit-to-unit damage, unit-to-tower damage, and tower-to-unit damage.

## Prerequisites
- Unity 2022.3 or later
- Basic understanding of Unity GameObjects and Components

## 1. Setting Up Units for Damage

### Step 1: Unit Health Component
Every unit that can take damage needs a `UnitHealth` component.

**For Each Unit Prefab:**
1. Select your unit prefab in the Project window
2. Open it in Prefab Mode (double-click)
3. Add the `UnitHealth` component if not present:
   - Click "Add Component"
   - Search for "UnitHealth"
   - Click to add it

**Configure UnitHealth:**
```
Max Health: 500 (adjust based on unit type)
Current Health: 0 (will auto-set to maxHealth)
Enable Networking: Check if using multiplayer
Smooth UI: Check for smooth health bar animations
UI Smooth Speed: 8.0
```

### Step 2: Unit Combat Component  
Make sure your unit has the `Unit` script attached and configured:

**Combat Settings:**
```
Move Speed: 3.0
Attack Range: 1.2 (melee) or 6.0 (ranged)
Attack Damage: 50 (adjust based on unit power)
Attack Cooldown: 1.0
Target Search Interval: 0.25
```

**Faction Settings:**
```
Faction: Player (for player units) or Enemy (for enemy units)
```

**Important:** Make sure the faction is set correctly - units will only attack opposite faction!

### Step 3: Unit Tags
Set the correct tags on your unit GameObjects:
- Player units: Tag = "Player" 
- Enemy units: Tag = "Enemy"

## 2. Setting Up Towers for Damage

### Step 1: Tower Health
Towers use the `Tower` script which has built-in health management.

**For Each Tower Prefab:**
1. Ensure the `Tower` component is attached
2. Configure the tower settings:

```
Tower Name: "Princess Tower" (or appropriate name)
Max Health: 2000 (Princess Tower), 4000 (King Tower)
Attack Range: 8.0
Attack Cooldown: 1.0
Damage Per Shot: 15
Owner Tag: "Player" or "Enemy"
Faction: Player or Enemy (should match owner tag)
```

### Step 2: King Tower Setup
King Towers should use the `KingTower` script instead of just `Tower`:

1. Remove `Tower` component if present
2. Add `KingTower` component
3. Configure:
```
King Tower Type: PlayerKing (for player) or EnemyKing (for enemy)
Base Max Health: 4000
Health Per Level: 400
Destroy All Units On Death: Check
```

**Note:** The King Tower Type dropdown automatically sets the correct ownerTag and faction for you!

### Step 3: Tower Colliders
Each tower needs a Collider component:
1. Add a Box Collider or Capsule Collider
2. Adjust size to match tower visual
3. Make sure "Is Trigger" is **UNCHECKED**

## 3. Setting Up Projectiles (For Ranged Units)

### Step 1: Projectile Prefab
Create a projectile prefab for ranged units:

1. Create empty GameObject
2. Add `Projectile` script
3. Add a visual (mesh/sprite)
4. Add Rigidbody component
5. Add Collider (set as Trigger)

**Projectile Settings:**
```
Damage: 25 (should match unit's attack damage)
Speed: 12.0
Lifetime: 5.0
Destroy On Hit: Check
Hit Effect Prefab: (optional explosion effect)
```

### Step 2: Configure Ranged Unit
For units that shoot projectiles:

```
Is Ranged: Check
Projectile Prefab: Assign your projectile prefab
Fire Point: Create empty child GameObject as muzzle point
Projectile Speed: 12.0 (should match projectile's speed)
```

## 4. Debugging the Damage System

### Step 1: Enable Debug Logs
The fixed scripts now include debug logging. In Unity Console, you should see:
- `[Unit] UnitName attacking tower TowerName`
- `[Unit] UnitName dealing X damage to target`
- `[Tower] TowerName taking X damage`
- `[UnitHealth] UnitName taking X damage from Source`

### Step 2: Check Common Issues

**Units Not Attacking:**
1. Verify factions are different (Player vs Enemy)
2. Check attack range is sufficient
3. Ensure units have clear line of sight
4. Verify tags are set correctly

**No Damage Being Dealt:**
1. Check UnitHealth component is present on targets
2. Verify faction settings
3. Ensure attack damage > 0
4. Look for debug logs in Console

**Towers Not Taking Damage:**
1. Ensure Tower script is attached
2. Check faction is set correctly
3. Verify TakeDamage method is being called (check logs)

## 5. Testing the Damage System

### Step 1: Create Test Scene
1. Create a simple scene with:
   - Player unit (faction = Player, tag = "Player")
   - Enemy tower (faction = Enemy, tag = "Enemy")
   - Enemy unit (faction = Enemy, tag = "Enemy")

### Step 2: Test Unit vs Tower
1. Place player unit near enemy tower
2. Play scene
3. Unit should move toward and attack tower
4. Check Console for damage logs
5. Tower health should decrease

### Step 3: Test Unit vs Unit
1. Place player unit and enemy unit near each other
2. Both should detect and attack each other
3. Check health bars decrease
4. Winner should survive

### Step 4: Test Tower vs Unit
1. Place enemy unit in range of player tower
2. Tower should automatically attack unit
3. Unit health should decrease

## 6. Common Configuration Mistakes

### Mistake 1: Wrong Factions
**Problem:** Units attacking their own team
**Solution:** Ensure Player units have faction=Player, Enemy units have faction=Enemy

### Mistake 2: Missing UnitHealth
**Problem:** Units not taking damage
**Solution:** Add UnitHealth component to all damageable units

### Mistake 3: Wrong Tags
**Problem:** Towers not finding targets
**Solution:** Set tags correctly (Player/Enemy) and ensure they match faction

### Mistake 4: Attack Range Too Low
**Problem:** Units not engaging in combat
**Solution:** Increase attack range or decrease unit spacing

### Mistake 5: Zero Attack Damage
**Problem:** Damage being dealt but no health lost
**Solution:** Set attackDamage > 0 on Unit component

## 7. Advanced Features

### Splash Damage Units
For units like MegaKnight that deal area damage:
```
Is Splash: Check
Splash Radius: 2.5
```

### Flying Units
For air units:
```
Is Flying Unit: Check
Can Attack Air Units: Check (if should attack other air units)
```

### Buff/Debuff Units
For support units:
```
Unit Role: Buffer/Debuffer/Healer
Effect Stat: AttackSpeed/MoveSpeed/AttackDamage/Health
Effect Amount: 0.5 (50% buff) or -0.3 (30% debuff)
Effect Mode: Aura or OnHit
```

## 8. Network Multiplayer Setup

If using multiplayer, ensure:
1. NetworkManager is in scene
2. Units have NetworkObject component
3. enableNetworking = true on UnitHealth and Tower components
4. NetworkObjects are properly spawned

## Conclusion

The damage system should now be working correctly. Use the debug logs to troubleshoot any issues, and ensure all components are properly configured with correct factions, tags, and damage values.

For further customization, you can modify the damage calculations in the `TryAttack()` method of the Unit script or the `TakeDamage()` methods in UnitHealth and Tower scripts.
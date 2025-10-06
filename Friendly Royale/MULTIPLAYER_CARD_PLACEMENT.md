# Enhanced Multiplayer Card Placement System

## Overview

The NetworkCardPlacementSystem has been enhanced to provide robust multiplayer card placement capabilities, allowing both players to place cards with proper validation, security, and visual feedback.

## Key Features

### 1. Network RPCs for Card Placement

#### `RequestCardPlacementServerRpc`
- Handles card placement requests from clients
- Performs server-side validation
- Integrates with NetworkSecurityManager for anti-cheat
- Spawns cards for all clients when valid
- Provides feedback to the requesting client

#### `SpawnCardForAllClientsClientRpc`
- Synchronizes card spawning across all clients
- Uses CardSpawner to handle actual unit/building creation
- Shows visual feedback for card placement by any player

#### `NotifyPlacementResultClientRpc`
- Provides feedback to the placing client about success/failure
- Can be extended for UI notifications and audio feedback

### 2. Enhanced Validation System

#### Client-Side Prediction
- `ValidatePositionOnClient()` - Fast validation for immediate feedback
- Prevents obvious invalid placements without server round-trip

#### Server-Side Authority
- `ValidatePositionOnServer()` - Comprehensive validation including:
  - Placement area validation (friendly/enemy zones)
  - Invalid area detection
  - Tower distance requirements
  - Building spacing rules
  - Ground detection
  - Faction-specific placement rules

### 3. Integration with Existing Systems

#### DraggableCard Integration
- Updated to use NetworkCardPlacementSystem.RequestCardPlacement()
- Falls back to direct CardSpawner for offline mode
- Enhanced validation during drag operations

#### HandUI Integration
- Click-based placement now uses NetworkCardPlacementSystem
- Maintains backward compatibility with offline play

### 4. Public API Methods

#### `RequestCardPlacement(Vector3 position, Card card, Unit.Faction playerFaction)`
- Main method for requesting card placement
- Handles both online and offline modes automatically
- Used by DraggableCard and HandUI systems

#### `TryGetPlacementPosition(Ray ray, Card card, out Vector3 worldPosition)`
- Compatible with CardPlacementSystem interface
- Provides real-time validation during drag operations
- Used for placement preview and validation

#### `IsNetworkReady()`
- Checks if the system can handle network requests
- Used to determine online vs offline behavior

## Usage Examples

### Basic Card Placement
```csharp
NetworkCardPlacementSystem networkPlacement = NetworkCardPlacementSystem.Instance;
if (networkPlacement != null)
{
    networkPlacement.RequestCardPlacement(worldPosition, cardData, Unit.Faction.Player);
}
```

### Validation During Drag
```csharp
Ray ray = camera.ScreenPointToRay(mousePosition);
Vector3 worldPos;
bool isValid = networkPlacement.TryGetPlacementPosition(ray, cardData, out worldPos);
```

### Check Network Status
```csharp
if (networkPlacement.IsNetworkReady())
{
    // Online multiplayer mode
}
else
{
    // Offline/single-player mode
}
```

## Security Features

### Server-Side Validation
- All placement requests are validated on the server
- Prevents client-side manipulation
- Rate limiting through NetworkSecurityManager integration

### Anti-Cheat Integration
- Works with existing NetworkSecurityManager
- Validates elixir costs and placement timing
- Detects suspicious placement patterns

## Visual Feedback

### Placement Indicators
- Shows valid/invalid placement areas during drag
- Color-coded feedback (green for valid, red for invalid)
- Compatible with existing CardDragPreview system

### Cross-Player Notifications
- Visual effects when other players place cards
- Can be extended with player identification
- Particle effects and UI notifications

## Configuration

### Layer Masks
- `friendlyPlacementLayerMask` - Areas where players can place cards
- `enemyPlacementLayerMask` - Enemy placement areas (for AI)
- `nonPlaceableLayerMask` - Blocked areas
- `groundLayerMask` - Valid ground surfaces

### Distance Settings
- `minDistanceFromTowers` - Tower exclusion radius
- `minDistanceBetweenBuildings` - Building spacing requirements
- `maxPlacementRange` - Maximum placement distance

### Visual Settings
- `validPlacementIndicator` - Prefab for valid placement feedback
- `invalidPlacementIndicator` - Prefab for invalid placement feedback
- `validColor` / `invalidColor` - Feedback colors

## Backward Compatibility

The system maintains full backward compatibility:
- Works in offline mode without network components
- Falls back to CardSpawner when NetworkCardPlacementSystem is unavailable
- Existing CardPlacementSystem interface is preserved

## Performance Considerations

### Client-Side Prediction
- Immediate visual feedback without server round-trip
- Reduces perceived latency during card placement

### Efficient Network Communication
- Only sends necessary data in RPCs
- Targeted ClientRpcs to specific clients when appropriate

### Optimized Validation
- Layered validation approach (client prediction + server authority)
- Cached references to reduce FindObject calls

## Future Enhancements

### Planned Features
- Spectator mode support
- Replay system integration
- Advanced visual effects for card placement
- Player-specific placement restrictions
- Tournament mode with enhanced anti-cheat

### Extension Points
- `ShowNetworkPlacementFeedback()` can be customized for visual effects
- `CheckFactionPlacementRules()` can be extended for game-specific rules
- `NotifyPlacementResultClientRpc()` can trigger custom UI responses
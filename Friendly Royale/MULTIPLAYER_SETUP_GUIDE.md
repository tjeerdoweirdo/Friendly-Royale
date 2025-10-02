# Friendly Royale - Multiplayer Setup Guide

## Overview
Your Clash Royale-style game has been converted to support full multiplayer functionality using Unity Netcode for GameObjects. This guide explains how to set up and use the new multiplayer features.

## What's Been Added

### 1. Core Networking Infrastructure
- **NetworkGameManager**: Handles multiplayer connections, player joining, and game session management
- **MultiplayerUI**: Provides UI for hosting/joining games and lobby functionality

### 2. Networked Game Systems
- **GameManager**: Converted to NetworkBehaviour with synchronized match state, timer, and results
- **NetworkCardSpawner**: Handles multiplayer card spawning with server validation
- **NetworkCardPlacementSystem**: Validates card placement across all clients

### 3. Networked Units and Buildings
- **NetworkUnit**: Multiplayer version of Unit with synchronized movement, combat, and state
- **NetworkTower**: Networked towers with synchronized health, attacks, and destruction
- **NetworkUnitHealth**: Server-authoritative health system preventing cheating

### 4. Player Progress and Security
- **NetworkPlayerProgress**: Synchronized player statistics, trophies, and rewards
- **NetworkSecurityManager**: Server-side validation system preventing cheating

## Setup Instructions

### Step 1: Unity Package Installation
The Unity Netcode for GameObjects package has been added to your project's `manifest.json`. 
After reopening Unity, the package will be automatically installed.

### Step 2: Create Network Manager GameObject
1. Create an empty GameObject in your main scene
2. Add the following components:
   - `NetworkManager` (from Unity Netcode)
   - `UnityTransport` (from Unity Netcode) 
   - `NetworkGameManager` (our custom script)
   - `NetworkSecurityManager` (our custom script)
3. Configure the NetworkManager:
   - Set Player Prefab to a prefab with NetworkObject component
   - Enable "Don't Destroy On Load"

### Step 3: Setup Multiplayer UI Scene
1. Create a new scene called "MultiplayerLobby"
2. Add a Canvas with the `MultiplayerUI` script
3. Setup UI elements:
   - Host Button
   - Join Button  
   - Server Button
   - Disconnect Button
   - IP Address Input Field
   - Port Input Field
   - Status Text
   - Player Count Text
   - Connection Panel
   - Lobby Panel

### Step 4: Convert Existing Prefabs
Replace your existing unit/tower prefabs with networked versions:

#### For Units:
1. Add `NetworkObject` component
2. Replace `Unit` with `NetworkUnit`
3. Replace `UnitHealth` with `NetworkUnitHealth`
4. Configure NetworkObject settings

#### For Towers:
1. Add `NetworkObject` component  
2. Replace `Tower` with `NetworkTower`
3. Configure NetworkObject settings

#### For Game Manager:
1. Your existing GameManager is now networked
2. It works in both single-player and multiplayer modes

### Step 5: Setup Card Spawning
1. Add `NetworkCardSpawner` to your scene
2. Configure spawn points and paths (same as original CardSpawner)
3. Update your card placement system to use `NetworkCardPlacementSystem`

### Step 6: Player Progress Setup
1. Add `NetworkPlayerProgress` component to player objects
2. The system automatically saves/loads progress using PlayerPrefs
3. In production, replace with proper database integration

## How to Use

### Starting a Multiplayer Game

#### As Host:
1. Enter IP address (127.0.0.1 for local testing)
2. Enter port (7777 is default)
3. Click "Host" button
4. Wait for another player to join
5. Click "Start Game" when ready

#### As Client:
1. Enter host's IP address
2. Enter same port as host
3. Click "Join" button
4. Wait in lobby for host to start

#### As Dedicated Server:
1. Click "Server" button to run headless server
2. Players can join as clients

### Testing Locally
1. Build the game
2. Run one instance as Host
3. Run another instance as Client with IP "127.0.0.1"
4. Both players should connect and be able to play

### Network Features

#### Synchronized Systems:
- Match timer and state
- Unit movement and combat
- Tower health and attacks
- Card placement and spawning
- Player statistics and progress

#### Security Features:
- Server-side validation for all actions
- Anti-cheat protection for resource spending
- Rate limiting to prevent spam
- Automatic suspension system for violators

#### Player Progress:
- Synchronized gold, trophies, and level
- Win/loss tracking
- Experience and level progression
- Arena/league system

## Backwards Compatibility

The system maintains full backwards compatibility:
- Original single-player mode still works
- Existing scripts can coexist with networked versions
- GameManager automatically detects single-player vs multiplayer

## Performance Considerations

- Network updates are optimized for 30-60 FPS
- Only essential data is synchronized
- Client-side prediction reduces lag
- Server authority prevents cheating

## Troubleshooting

### Common Issues:
1. **Can't connect**: Check firewall settings and port forwarding
2. **Lag**: Ensure stable network connection
3. **Desync**: Server validates all actions to maintain sync
4. **Cheating**: SecurityManager automatically detects and prevents

### Debug Tools:
- Check console for network logs
- Use Unity Profiler for performance monitoring
- NetworkSecurityManager provides violation reports

## Next Steps

### For Production:
1. Replace PlayerPrefs with proper database
2. Add dedicated server hosting
3. Implement proper matchmaking
4. Add reconnection handling
5. Optimize for mobile networks

### Additional Features:
- Spectator mode
- Tournament system
- Clan battles
- Real-time chat
- Replay system

## Files Created/Modified

### New Files:
- `NetworkGameManager.cs` - Core multiplayer management
- `MultiplayerUI.cs` - Lobby and connection UI
- `NetworkCardSpawner.cs` - Multiplayer card spawning
- `NetworkCardPlacementSystem.cs` - Networked placement validation
- `NetworkUnit.cs` - Multiplayer unit behavior
- `NetworkTower.cs` - Multiplayer tower system
- `NetworkUnitHealth.cs` - Server-authoritative health
- `NetworkPlayerProgress.cs` - Synchronized player stats
- `NetworkSecurityManager.cs` - Anti-cheat system

### Modified Files:
- `GameManager.cs` - Added networking support
- `Packages/manifest.json` - Added Netcode package

Your game is now fully multiplayer-capable and ready for online Clash Royale-style battles!
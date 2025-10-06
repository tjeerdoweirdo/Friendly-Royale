# Opponent Username Display Setup Guide

## Overview

The MatchmakingManager now supports displaying opponent usernames when matches are found. This guide explains how to set up the UI elements and use the new functionality.

## UI Elements Required

### 1. Opponent Username Text (Required)
- **Component**: `TMP_Text` 
- **Field**: `opponentUsernameText`
- **Purpose**: Displays "vs [OpponentName]" when opponent is found
- **Example Text**: "vs DragonSlayer"

### 2. Opponent Info Panel (Optional)
- **Component**: `GameObject`
- **Field**: `opponentInfoPanel` 
- **Purpose**: Container panel that shows/hides when opponent is found
- **Content**: Can contain opponent name, trophies, additional info

## MatchmakingManager Setup

### Inspector Fields
Add these new fields to your MatchmakingManager inspector:

```
[Header("Opponent Display")]
[Tooltip("Text showing opponent's username when found")]
public TMP_Text opponentUsernameText;

[Tooltip("Panel showing opponent info when match is found")]
public GameObject opponentInfoPanel;
```

### Example UI Hierarchy
```
MatchmakingPanel
├── StatusText (existing)
├── FindMatchButton (existing)  
├── CancelMatchButton (existing)
├── OpponentInfoPanel (NEW)
│   ├── OpponentUsernameText (NEW)
│   ├── OpponentTrophiesText (optional)
│   └── VS_Icon (optional)
└── Other elements...
```

## How It Works

### Simulated Matchmaking (Offline/Testing)
```csharp
// Generates random opponent from predefined list
string[] possibleNames = {
    "DragonSlayer", "KnightRider", "WizardMaster", "ArcherQueen",
    "GoblinKing", "SteelWarrior", "FireMage", "IceWizard"
    // ... more names
};

// Shows: "vs DragonSlayer (1250🏆)"
```

### Real Multiplayer
```csharp
// Extracts opponent data from Unity Lobby Service
- Username from PlayerProgress.GetUsername()
- Trophies from player data
- Fallback: "Player_1234" if no username set
```

## Features

### Automatic Username Generation
- Uses player's saved username from PlayerProgress
- Falls back to "Player_XXXX" with random numbers
- Handles empty/null usernames gracefully

### Opponent Information Display
- **Username**: Actual player name or generated fallback
- **Trophies**: Opponent's current trophy count  
- **Status**: "Opponent found: [Name] ([Trophies]🏆)"

### UI State Management
- Shows opponent info when match found
- Hides info when matchmaking cancelled
- Clears info when starting new search

## Code Examples

### Check if opponent found
```csharp
if (!string.IsNullOrEmpty(opponentUsername))
{
    Debug.Log($"Playing against: {opponentUsername}");
}
```

### Custom opponent display
```csharp
void UpdateOpponentDisplay()
{
    if (opponentUsernameText != null)
    {
        opponentUsernameText.text = $"vs {opponentUsername}";
        
        // Add trophy display
        if (opponentTrophies > 0)
        {
            opponentUsernameText.text += $" ({opponentTrophies}🏆)";
        }
    }
}
```

### Handle opponent info events
```csharp
void OnOpponentFound(string username, int trophies)
{
    // Custom logic when opponent is found
    Debug.Log($"Matched with {username} who has {trophies} trophies");
    
    // Could trigger animations, sounds, etc.
    PlayMatchFoundSound();
    ShowMatchFoundAnimation();
}
```

## Testing

### Simulated Mode (Default)
1. Click "Find Match" 
2. Wait ~6 seconds (40% of simulated time)
3. Should show: "Opponent found: [RandomName] ([Trophies]🏆)"
4. OpponentUsernameText displays: "vs [RandomName]"

### Real Multiplayer Mode
1. Requires Unity Lobby Service setup
2. Uses actual player usernames
3. Shows real opponent trophy counts
4. Falls back to generated names if needed

## Customization Options

### Change Random Names
Edit the `possibleNames` array in `GenerateSimulatedOpponent()`:
```csharp
string[] possibleNames = {
    "YourCustomName1",
    "YourCustomName2", 
    // Add your preferred names
};
```

### Modify Display Format
Update `ShowOpponentFound()` method:
```csharp
void ShowOpponentFound()
{
    if (opponentUsernameText != null)
    {
        // Custom format: "Opponent: Name (Trophies)"
        opponentUsernameText.text = $"Opponent: {opponentUsername} ({opponentTrophies})";
    }
}
```

### Add More Opponent Info
Extend the opponent data structure:
```csharp
// Add new fields
private string opponentArena = "";
private int opponentLevel = 1;
private float opponentWinRate = 0f;

// Update display
void ShowDetailedOpponentInfo()
{
    string info = $"{opponentUsername}\n";
    info += $"🏆 {opponentTrophies}\n";  
    info += $"⭐ Level {opponentLevel}\n";
    info += $"📊 {(opponentWinRate * 100):F1}% Win Rate";
    
    detailedInfoText.text = info;
}
```

## Integration with PlayerProgress

The system automatically uses the player's username from PlayerProgress:
```csharp
// Set username (saved automatically)
PlayerProgress.Instance.SetUsername("MyAwesomeName");

// Get username (used in matchmaking)
string name = PlayerProgress.Instance.GetUsername();
```

Make sure players can set their username in your game's settings/profile screen for the best multiplayer experience!
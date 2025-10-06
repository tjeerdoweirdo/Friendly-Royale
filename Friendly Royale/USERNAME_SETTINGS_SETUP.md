# Username Settings Setup Guide

## Overview

The Settings panel now includes a username input field that integrates with the PlayerProgress system, allowing players to customize their display name for multiplayer matches.

## UI Setup

### Inspector Fields
Add this new field to your Settingspanel inspector:

```
[Header("UI Elements")]
public TMP_InputField usernameInputField; // For editing player username
```

### Example UI Hierarchy
```
SettingsPanel
├── ResolutionDropdown (existing)
├── GraphicsDropdown (existing)  
├── VolumeSlider (existing)
├── UsernameSection (NEW)
│   ├── UsernameLabel (TMP_Text: "Username:")
│   └── UsernameInputField (TMP_InputField)
├── SaveSettingsButton (existing)
└── ResetSettingsButton (existing)
```

## Features Implemented

### 1. **Username Input Field**
- **Component**: `TMP_InputField`
- **Character Limit**: 16 characters maximum
- **Content Type**: Standard (allows letters, numbers, symbols)
- **Auto-trim**: Removes leading/trailing spaces

### 2. **Real-time Validation**
- **Length Check**: Automatically truncates if over 16 characters
- **Trim Spaces**: Removes extra whitespace
- **Live Updates**: Validates as user types

### 3. **PlayerProgress Integration**
- **Load Username**: Displays current saved username on panel open
- **Save Username**: Persists changes to PlayerProgress system
- **Fallback Handling**: Handles empty/null usernames gracefully

### 4. **Save System Integration**
- **Auto-save**: Username saved when "Save Settings" button pressed
- **Immediate Save**: Username also saved when PlayerProgress.SetUsername() called
- **Reset Support**: Username cleared when "Reset Settings" pressed

## Methods Added

### Public Methods
```csharp
public void SetUsername(string username)
// Sets username in PlayerProgress and updates UI

public string GetUsername()
// Gets current username from PlayerProgress

public void SaveSettings()
// Updated to include username saving
```

### Private Methods
```csharp
void SetupUsernameInput()
// Initializes the input field with current username

void ValidateUsername(string username)
// Real-time validation as user types

void OnUsernameEditEnd(string username)
// Called when user finishes editing

void SaveUsername()
// Saves username to PlayerProgress system
```

## Usage Examples

### Setting Up the Input Field
1. Add a `TMP_InputField` to your Settings panel
2. Assign it to the `usernameInputField` field in the inspector
3. The system will automatically:
   - Load the current username
   - Set character limits
   - Add validation listeners

### Manual Username Setting
```csharp
// Set username programmatically
settingsPanel.SetUsername("MyAwesomeUsername");

// Get current username
string currentName = settingsPanel.GetUsername();
```

### Integration with Matchmaking
The username system works seamlessly with the MatchmakingManager:
```csharp
// MatchmakingManager automatically uses PlayerProgress.GetUsername()
string playerName = playerProgress.GetUsername();

// Falls back to generated name if empty
if (string.IsNullOrEmpty(playerName))
{
    playerName = "Player_" + Random.Range(1000, 9999);
}
```

## Validation Rules

### Character Limits
- **Maximum Length**: 16 characters
- **Minimum Length**: 1 character (cannot be empty)
- **Auto-truncation**: Text automatically cut if too long

### Content Filtering
- **Allowed**: Letters, numbers, spaces, common symbols
- **Trimming**: Leading/trailing spaces removed
- **Real-time**: Validation happens as user types

### Error Handling
- **Empty Username**: Prevents saving empty usernames
- **Null PlayerProgress**: Logs warning if PlayerProgress not found
- **UI Safety**: Checks for null components before using

## UI Design Tips

### Visual Layout
```
Username Settings
┌─────────────────────────────┐
│ Username: [MyUsername     ] │
│                             │
│ • Max 16 characters         │
│ • Used in multiplayer       │
└─────────────────────────────┘
```

### Input Field Configuration
- **Placeholder Text**: "Enter your username..."
- **Text Color**: Use theme colors for consistency
- **Border**: Highlight border when active
- **Font**: Match other UI elements

### User Experience
- **Auto-focus**: Input field selected when panel opens
- **Enter Key**: Save username when Enter pressed
- **Visual Feedback**: Show character count if desired
- **Validation Messages**: Optional error messages for invalid input

## Integration with Existing Systems

### PlayerProgress System
```csharp
// Username is automatically saved to PlayerPrefs
PlayerProgress.Instance.SetUsername("NewUsername");

// Retrieved for matchmaking and display
string username = PlayerProgress.Instance.GetUsername();
```

### Matchmaking System
```csharp
// Used in lobby creation and opponent display
GetPlayerUsername() // Gets from PlayerProgress
ShowOpponentFound() // Displays opponent username
```

### Settings Persistence
```csharp
// Username saved with other settings
SaveSettings() // Includes username in save operation
ResetSettings() // Clears username to empty
```

## Customization Options

### Extend Character Limits
```csharp
usernameInputField.characterLimit = 20; // Increase to 20 characters
```

### Add Profanity Filtering
```csharp
void ValidateUsername(string username)
{
    // Add custom filtering logic
    if (ContainsProfanity(username))
    {
        // Handle inappropriate content
    }
}
```

### Custom Input Validation
```csharp
void ValidateUsername(string username)
{
    // Only allow alphanumeric characters
    string filtered = System.Text.RegularExpressions.Regex.Replace(username, @"[^a-zA-Z0-9]", "");
    if (filtered != username)
    {
        usernameInputField.text = filtered;
    }
}
```

### Visual Feedback
```csharp
void OnUsernameEditEnd(string username)
{
    // Show success/error feedback
    if (username.Length >= 3)
    {
        ShowSuccessMessage("Username saved!");
    }
    else
    {
        ShowErrorMessage("Username too short!");
    }
}
```

## Best Practices

1. **Always validate input** before saving
2. **Provide visual feedback** for user actions
3. **Handle edge cases** (empty, null, too long)
4. **Test integration** with multiplayer systems
5. **Consider accessibility** (screen readers, etc.)

This username system provides a complete solution for player name customization while maintaining integration with the existing PlayerProgress and matchmaking systems!
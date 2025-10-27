using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;

/// <summary>
/// Lightweight presence publisher: while active (e.g., on the Main Menu),
/// joins or creates a small public lobby and keeps your player listed with
/// username + trophies so other players can see you in OnlineUsersPanel.
/// </summary>
public class PresenceManager : MonoBehaviour
{
    [Header("Presence Lobby Settings")]
    [Tooltip("Prefix used to name presence lobbies")] public string lobbyNamePrefix = "FR-Presence";
    [Tooltip("Max players in a presence lobby")] public int maxPlayers = 50;
    [Tooltip("Seconds between heartbeat pings (owner only)")] public float heartbeatIntervalSeconds = 15f;

    [Header("Player Data Sources")] 
    [Tooltip("Optional: provide username directly; else attempts PlayerPrefs/PlayerProgress")] public string overrideUsername = "";
    [Tooltip("Optional: provide trophies directly; else attempts PlayerPrefs key 'Trophies'")] public int overrideTrophies = -1;

    private Lobby _lobby;
    private Coroutine _heartbeatCo;
    private bool _isOwner;

    private async void OnEnable()
    {
        // Initialize Services and Sign-in
        try
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync();
            }
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[PresenceManager] Unity Services init failed: {e.Message}");
            return;
        }

        // Try join any existing presence lobby with our prefix
        try
        {
            var queryOpts = new QueryLobbiesOptions
            {
                Count = 25
            };
            var query = await LobbyService.Instance.QueryLobbiesAsync(queryOpts);
            Lobby target = null;
            foreach (var l in query.Results)
            {
                if (l == null || l.IsPrivate) continue;
                if (!string.IsNullOrEmpty(l.Name) && l.Name.StartsWith(lobbyNamePrefix))
                {
                    // Pick the one with most available slots
                    if (target == null || l.AvailableSlots > target.AvailableSlots)
                        target = l;
                }
            }

            var player = BuildPlayerData();

            if (target != null)
            {
                _lobby = await LobbyService.Instance.JoinLobbyByIdAsync(target.Id, new JoinLobbyByIdOptions { Player = player });
                _isOwner = false;
                Debug.Log($"[PresenceManager] Joined presence lobby '{_lobby.Name}' ({_lobby.Id}), players={_lobby.Players?.Count}");
            }
            else
            {
                string lobbyName = $"{lobbyNamePrefix}-{Random.Range(1000, 9999)}";
                var createOpts = new CreateLobbyOptions
                {
                    IsPrivate = false,
                    Player = player,
                    Data = new Dictionary<string, DataObject>
                    {
                        {"type", new DataObject(DataObject.VisibilityOptions.Public, "presence")}
                    }
                };
                _lobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, Mathf.Max(2, maxPlayers), createOpts);
                _isOwner = true;
                Debug.Log($"[PresenceManager] Created presence lobby '{_lobby.Name}' ({_lobby.Id})");
            }

            if (_isOwner && _lobby != null)
            {
                _heartbeatCo = StartCoroutine(HeartbeatLoop());
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[PresenceManager] Failed to join/create presence lobby: {e.Message}");
        }
    }

    private async void OnDisable()
    {
        if (_heartbeatCo != null)
        {
            StopCoroutine(_heartbeatCo);
            _heartbeatCo = null;
        }

        if (_lobby != null)
        {
            try
            {
                await LobbyService.Instance.RemovePlayerAsync(_lobby.Id, AuthenticationService.Instance.PlayerId);
                Debug.Log("[PresenceManager] Left presence lobby");
            }
            catch { /* ignore */ }
            _lobby = null;
        }
    }

    private IEnumerator HeartbeatLoop()
    {
        var wait = new WaitForSecondsRealtime(Mathf.Max(5f, heartbeatIntervalSeconds));
        while (_lobby != null)
        {
            var task = LobbyService.Instance.SendHeartbeatPingAsync(_lobby.Id);
            while (!task.IsCompleted) yield return null;
            yield return wait;
        }
    }

    private Unity.Services.Lobbies.Models.Player BuildPlayerData()
    {
        string username = !string.IsNullOrEmpty(overrideUsername) ? overrideUsername : GetUsernameFallback();
        int trophies = overrideTrophies >= 0 ? overrideTrophies : GetTrophiesFallback();
        return new Unity.Services.Lobbies.Models.Player
        {
            Data = new Dictionary<string, PlayerDataObject>
            {
                {"username", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, username) },
                {"trophies", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, Mathf.Max(0, trophies).ToString()) }
            }
        };
    }

    private string GetUsernameFallback()
    {
        // Try PlayerPrefs, else anonymous short id
        var up = PlayerPrefs.GetString("Username", string.Empty);
        if (!string.IsNullOrWhiteSpace(up)) return up;
        string pid = AuthenticationService.Instance != null && AuthenticationService.Instance.IsSignedIn ? AuthenticationService.Instance.PlayerId : System.Guid.NewGuid().ToString("N");
        return $"Player_{pid.Substring(0, 6)}";
    }

    private int GetTrophiesFallback()
    {
        return PlayerPrefs.GetInt("Trophies", 0);
    }
}

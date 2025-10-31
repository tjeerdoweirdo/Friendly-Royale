using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Lightweight telemetry bus to log client matchmaking state to the server console.
/// Uses NGO Custom Messaging (no NetworkObjects needed).
/// </summary>
public class NetworkStatusTelemetry : MonoBehaviour
{
    private static NetworkStatusTelemetry _instance;
    private const string MsgName = "match_status";

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
        TryRegisterHandlers();
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted += TryRegisterHandlers;
            NetworkManager.Singleton.OnClientStarted += TryRegisterHandlers;
        }
    }

    void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted -= TryRegisterHandlers;
            NetworkManager.Singleton.OnClientStarted -= TryRegisterHandlers;
        }
        if (_instance == this) _instance = null;
    }

    private void TryRegisterHandlers()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.CustomMessagingManager == null) return;
        // Server handler: log incoming client state
        if (nm.IsServer)
        {
            try
            {
                nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgName, (senderClientId, reader) =>
                {
                    try
                    {
                        using (reader)
                        {
                            string user; reader.ReadValueSafe(out user);
                            int state; reader.ReadValueSafe(out state);
                            bool inReady; reader.ReadValueSafe(out inReady);
                            bool localReady; reader.ReadValueSafe(out localReady);
                            bool oppReady; reader.ReadValueSafe(out oppReady);
                            string opp; reader.ReadValueSafe(out opp);
                            Debug.Log($"[MatchTelemetry] cid={senderClientId} user='{user}' state={(MatchState)state} readyPhase={inReady} you={localReady} opp={oppReady} oppName='{opp}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[MatchTelemetry] parse error: {ex.Message}");
                    }
                });
            }
            catch { /* already registered */ }
        }
        // Server connect/disconnect diagnostics
        try
        {
            nm.OnClientConnectedCallback -= OnClientConnected;
            nm.OnClientDisconnectCallback -= OnClientDisconnected;
            nm.OnClientConnectedCallback += OnClientConnected;
            nm.OnClientDisconnectCallback += OnClientDisconnected;
        }
        catch { }
    }

    private void OnClientConnected(ulong cid)
    {
        if (!NetworkManager.Singleton.IsServer) return;
        var nm = NetworkManager.Singleton;
        int count = nm.ConnectedClientsIds.Count;
        Debug.Log($"[Server] Client connected: {cid}. Total={count}");
    }

    private void OnClientDisconnected(ulong cid)
    {
        if (!NetworkManager.Singleton.IsServer) return;
        var nm = NetworkManager.Singleton;
        int count = nm.ConnectedClientsIds.Count;
        Debug.Log($"[Server] Client disconnected: {cid}. Total={count}");
    }

    public enum MatchState { Idle=0, ValidatingDeck=1, SearchingForMatch=2, FoundMatch=3, JoiningMatch=4, Error=5 }

    public static void ReportMatchmakingState(string username, MatchState state, bool inReadyPhase, bool localReady, bool opponentReady, string opponentName)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.CustomMessagingManager == null || !nm.IsClient) return;
        try
        {
            var cmm = nm.CustomMessagingManager;
            using (var writer = new FastBufferWriter(256, Allocator.Temp))
            {
                writer.WriteValueSafe(username ?? string.Empty);
                writer.WriteValueSafe((int)state);
                writer.WriteValueSafe(inReadyPhase);
                writer.WriteValueSafe(localReady);
                writer.WriteValueSafe(opponentReady);
                writer.WriteValueSafe(opponentName ?? string.Empty);
                cmm.SendNamedMessage(MsgName, NetworkManager.ServerClientId, writer);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MatchTelemetry] send failed: {ex.Message}");
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (_instance == null)
        {
            var go = new GameObject("NetworkStatusTelemetry");
            go.hideFlags = HideFlags.DontSave;
            go.AddComponent<NetworkStatusTelemetry>();
        }
    }
}

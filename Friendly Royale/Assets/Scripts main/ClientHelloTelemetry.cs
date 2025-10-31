using System;
using UnityEngine;
using Unity.Netcode;

public class ClientHelloTelemetry : MonoBehaviour
{
    private static ClientHelloTelemetry _instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (_instance == null)
        {
            var go = new GameObject("ClientHelloTelemetry");
            go.hideFlags = HideFlags.DontSave;
            _instance = go.AddComponent<ClientHelloTelemetry>();
            DontDestroyOnLoad(go);
        }
    }

    void Awake()
    {
        TryHook();
    }

    void OnEnable() => TryHook();

    private void TryHook()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        nm.OnClientConnectedCallback -= OnClientConnected;
        nm.OnClientConnectedCallback += OnClientConnected;
    }

    private void OnClientConnected(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        // Only send hello from the local client side
        if (nm.IsClient && !nm.IsServer && clientId == nm.LocalClientId)
        {
            string username = PlayerPrefs.GetString("LocalPlayerUsername", string.Empty);
            if (string.IsNullOrEmpty(username)) username = "Player_" + UnityEngine.Random.Range(1000, 9999);
            try
            {
                NetworkStatusTelemetry.ReportMatchmakingState(username, NetworkStatusTelemetry.MatchState.Idle, false, false, false, string.Empty);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HelloTelemetry] failed to send: " + ex.Message);
            }
        }
    }
}

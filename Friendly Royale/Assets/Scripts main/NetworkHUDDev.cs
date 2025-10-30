using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

/// <summary>
/// Minimal on-screen HUD for Netcode StartHost/StartClient/Shutdown and status.
/// Only auto-spawned in Editor or Development builds.
/// </summary>
public class NetworkHUDDev : MonoBehaviour
{
    private Rect _rect = new Rect(10, 10, 220, 180);
    private bool _visible = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Skip if already present
        if (FindAnyObjectByType<NetworkHUDDev>() != null) return;
        var go = new GameObject("NetworkHUD_DEV");
        go.AddComponent<NetworkHUDDev>();
        DontDestroyOnLoad(go);
#endif
    }

    void Update()
    {
        // Toggle HUD visibility
        if (Input.GetKeyDown(KeyCode.F8))
        {
            _visible = !_visible;
        }

        // Quick shutdown hotkey even if window hidden
        if (Input.GetKeyDown(KeyCode.F9))
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening)
            {
                nm.Shutdown();
            }
        }
    }

    void OnGUI()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!_visible) return;
        _rect = GUI.Window(3942, _rect, DrawWindow, "Network");
#endif
    }

    private void DrawWindow(int id)
    {
        var nm = NetworkManager.Singleton;
        GUILayout.BeginVertical();
        try
        {
            GUILayout.Label(nm == null ? "No NetworkManager" : StatusString(nm));
            GUILayout.Space(6);

            // Relay protocol preference
            string proto = PlayerPrefs.GetString("RelayProtocol", "wss");
            GUILayout.Label($"Relay Protocol: {proto}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Use WSS")) { PlayerPrefs.SetString("RelayProtocol", "wss"); PlayerPrefs.Save(); }
            if (GUILayout.Button("Use DTLS")) { PlayerPrefs.SetString("RelayProtocol", "dtls"); PlayerPrefs.Save(); }
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            GUI.enabled = nm != null && !nm.IsListening;
            if (GUILayout.Button("Start Host"))
            {
                EnsureTransport(nm);
                ConfigureConnectionForServer(nm); // host listens on server endpoint
                nm.StartHost();
            }
            if (GUILayout.Button("Start Client"))
            {
                EnsureTransport(nm);
                ConfigureConnectionForClient(nm);
                nm.StartClient();
            }
            if (GUILayout.Button("Start Server"))
            {
                EnsureTransport(nm);
                ConfigureConnectionForServer(nm);
                nm.StartServer();
            }
            GUI.enabled = true;

            GUI.enabled = nm != null && nm.IsListening;
            if (GUILayout.Button("Shutdown"))
            {
                nm.Shutdown();
            }
            GUI.enabled = true;
        }
        finally
        {
            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0,0, 10000, 20));
        }
    }

    private static void EnsureTransport(NetworkManager nm)
    {
        if (nm == null) return;
        if (nm.GetComponent<UnityTransport>() == null)
        {
            nm.gameObject.AddComponent<UnityTransport>();
        }
    }

    private static void ConfigureConnectionForServer(NetworkManager nm)
    {
        var utp = nm != null ? nm.GetComponent<UnityTransport>() : null;
        if (utp == null) return;
        // Bind server/host on all interfaces at default dev port
        string ip = PlayerPrefs.GetString("Net_IP_Server", "0.0.0.0");
        ushort port = (ushort)PlayerPrefs.GetInt("Net_Port", 7777);
        try { utp.SetConnectionData(ip, port); } catch { }
    }

    private static void ConfigureConnectionForClient(NetworkManager nm)
    {
        var utp = nm != null ? nm.GetComponent<UnityTransport>() : null;
        if (utp == null) return;
        // Connect to local host by default in dev HUD
        string ip = PlayerPrefs.GetString("Net_IP_Client", "127.0.0.1");
        ushort port = (ushort)PlayerPrefs.GetInt("Net_Port", 7777);
        try { utp.SetConnectionData(ip, port); } catch { }
    }

    private static string StatusString(NetworkManager nm)
    {
        if (nm == null) return "No NetworkManager";
        int clientCount = 0;
        try
        {
            // ConnectedClients throws on non-server; ConnectedClientsIds works on both
            clientCount = (nm != null && nm.IsListening && nm.ConnectedClientsIds != null) ? nm.ConnectedClientsIds.Count : 0;
        }
        catch { clientCount = 0; }
        return $"Srv:{nm.IsServer} Cl:{nm.IsClient} Host:{nm.IsHost} Listening:{nm.IsListening}\nClients:{clientCount}";
    }
}

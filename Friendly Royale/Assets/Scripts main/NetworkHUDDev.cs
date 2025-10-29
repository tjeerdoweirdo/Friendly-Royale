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
        GUILayout.Label(nm == null ? "No NetworkManager" : StatusString(nm));
        GUILayout.Space(6);

        GUI.enabled = nm != null && !nm.IsListening;
        if (GUILayout.Button("Start Host"))
        {
            EnsureTransport(nm);
            nm.StartHost();
        }
        if (GUILayout.Button("Start Client"))
        {
            EnsureTransport(nm);
            nm.StartClient();
        }
        if (GUILayout.Button("Start Server"))
        {
            EnsureTransport(nm);
            nm.StartServer();
        }
        GUI.enabled = true;

        GUI.enabled = nm != null && nm.IsListening;
        if (GUILayout.Button("Shutdown"))
        {
            nm.Shutdown();
        }
        GUI.enabled = true;

        GUILayout.EndVertical();
        GUI.DragWindow(new Rect(0,0, 10000, 20));
    }

    private static void EnsureTransport(NetworkManager nm)
    {
        if (nm == null) return;
        if (nm.GetComponent<UnityTransport>() == null)
        {
            nm.gameObject.AddComponent<UnityTransport>();
        }
    }

    private static string StatusString(NetworkManager nm)
    {
        if (nm == null) return "No NetworkManager";
        return $"Srv:{nm.IsServer} Cl:{nm.IsClient} Host:{nm.IsHost} Listening:{nm.IsListening}\nClients:{nm.ConnectedClients?.Count ?? 0}";
    }
}

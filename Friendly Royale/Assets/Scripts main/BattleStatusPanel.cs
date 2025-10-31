using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

/// <summary>
/// Lightweight IMGUI panel toggled with Tab in battle to show opponent connection,
/// mirroring status, and transport/server info. Also exposes a few quick toggles.
/// </summary>
public class BattleStatusPanel : MonoBehaviour
{
    public KeyCode toggleKey = KeyCode.Tab;
    public bool visible = false;
    public Rect windowRect = new Rect(20, 20, 460, 320);

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            visible = !visible;
        }
    }

    void OnGUI()
    {
        if (!visible) return;
        try
        {
            windowRect = GUI.Window(1972057, windowRect, DrawWindow, "Battle Status");
        }
        catch { /* IMGUI safety */ }
    }

    private void DrawWindow(int id)
    {
        var nm = NetworkManager.Singleton;
        var utp = nm ? nm.GetComponent<UnityTransport>() : null;
        bool listening = nm && nm.IsListening;
        bool isHost = nm && nm.IsHost;
        bool isServer = nm && nm.IsServer;
        bool isClient = nm && nm.IsClient;
        ulong localId = nm ? nm.LocalClientId : 0UL;

        GUILayout.Label($"Net: {(listening ? "LISTENING" : "OFFLINE")} | Role: {(isHost?"Host":(isServer?"Server":(isClient?"Client":"None")))} | LocalId: {localId}");
        if (utp != null)
        {
            GUILayout.Label($"Transport: UnityTransport | Address={(isClient?PlayerPrefs.GetString("Net_IP_Client","127.0.0.1"):PlayerPrefs.GetString("Net_IP_Server","0.0.0.0"))}:{PlayerPrefs.GetInt("Net_Port",7777)}");
        }
        string relayProto = PlayerPrefs.GetString("RelayProtocol", "wss");
        GUILayout.Label($"Relay protocol: {relayProto}");

        // Opponent info
        string oppName = PlayerPrefs.GetString("OpponentUsername", "<unknown>");
        int connectedClients = nm ? nm.ConnectedClientsIds.Count : 0;
        bool opponentConnected = false;
        if (nm)
        {
            if (nm.IsHost || nm.IsServer)
            {
                // Host considers opponent connected if any client (id != ServerClientId) is present
                foreach (var cid in nm.ConnectedClientsIds)
                {
                    if (cid != NetworkManager.ServerClientId) { opponentConnected = true; break; }
                }
            }
            else if (nm.IsClient)
            {
                // For a client, being connected to the server implies the opponent (host) is present
                opponentConnected = nm.IsConnectedClient;
            }
        }
        GUILayout.Space(6);
        GUILayout.Label($"Opponent: {oppName} | Connected: {(opponentConnected?"YES":"NO")}");
        // Show raw client list for debugging
        if (nm)
        {
            string ids = string.Join(", ", nm.ConnectedClientsIds);
            GUILayout.Label($"Clients: {connectedClients} | IDs: [{ids}] | Local={nm.LocalClientId} Server={NetworkManager.ServerClientId}");
        }
        else
        {
            GUILayout.Label("Clients: 0");
        }

        // Placement/mirroring info and toggles
        var ncps = FindAnyObjectByType<NetworkCardPlacementSystem>();
        var spawner = FindAnyObjectByType<CardSpawner>();
        int spawnedNcps = 0;
        if (ncps != null)
        {
            var allSys = FindObjectsByType<NetworkCardPlacementSystem>(FindObjectsSortMode.None);
            foreach (var s in allSys)
            {
                var no = s.GetComponent<NetworkObject>();
                if (no != null && no.IsSpawned) spawnedNcps++;
            }
        }

        GUILayout.Space(6);
        GUILayout.Label($"NCPS: {(ncps?"present":"missing")} | Spawned count: {spawnedNcps}");
        GUILayout.Label($"Spawner: {(spawner?"present":"missing")} | alwaysClientSideSpawn={(spawner?spawner.alwaysClientSideSpawn:false)}");
        bool map = ncps ? ncps.mapOpponentPositions : false;
        var mode = ncps ? ncps.opponentMappingMode.ToString() : "<n/a>";
        GUILayout.Label($"Mirroring: mapOpponentPositions={map} | mode={mode}");

        // Show last enemy placement in the panel (no TMP overlay)
        Vector3? origPos = NetworkCardPlacementSystem.LastEnemyPlacementOriginal;
        Vector3? mappedPos = NetworkCardPlacementSystem.LastEnemyPlacementMapped;
        string lastCard = NetworkCardPlacementSystem.LastEnemyPlacementCardId ?? "<card>";
        double lastTime = NetworkCardPlacementSystem.LastEnemyPlacementTime;
        string when = lastTime > 0 ? $"{(Time.timeAsDouble - lastTime):0.0}s ago" : "n/a";
        if (origPos.HasValue)
        {
            var o = origPos.Value;
            var m = mappedPos ?? origPos;
            GUILayout.Label($"Last enemy placement: {lastCard} | orig ({o.x:F1},{o.z:F1}) -> mapped ({m.Value.x:F1},{m.Value.z:F1}) | {when}");
        }
        else
        {
            GUILayout.Label("Last enemy placement: <none>");
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button(map ? "Disable Mirroring" : "Enable Mirroring", GUILayout.Height(24)))
        {
            if (ncps) ncps.mapOpponentPositions = !map;
        }
        if (GUILayout.Button("Cycle Mirror Mode", GUILayout.Height(24)))
        {
            if (ncps)
            {
                int next = ((int)ncps.opponentMappingMode + 1) % System.Enum.GetValues(typeof(NetworkCardPlacementSystem.OpponentMappingMode)).Length;
                ncps.opponentMappingMode = (NetworkCardPlacementSystem.OpponentMappingMode)next;
            }
        }
        if (GUILayout.Button("Toggle Client-Side Spawn", GUILayout.Height(24)))
        {
            if (spawner) spawner.alwaysClientSideSpawn = !spawner.alwaysClientSideSpawn;
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(6);
        GUILayout.Label("Tips: Mirroring affects how opponent placements are seen locally.\nClient-side spawn helps when prefabs lack NetworkObject.");

        GUI.DragWindow(new Rect(0,0, 10000, 20));
    }
}

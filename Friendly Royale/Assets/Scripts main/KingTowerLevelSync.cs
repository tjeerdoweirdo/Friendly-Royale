using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Synchronizes King Tower levels between host and client so that:
/// - Each client uses their own PlayerProgress for their own king (local player level)
/// - Each client receives the opponent's king level from the server and applies it to the Enemy king
/// This avoids both kings mirroring the same local level.
/// </summary>
public class KingTowerLevelSync : NetworkBehaviour
{
    private int serverReportedLevel = -1;
    private int clientReportedLevel = -1;
    private bool applied;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsClient)
        {
            int myLevel = 1;
            if (PlayerProgress.Instance != null)
            {
                myLevel = Mathf.Max(1, PlayerProgress.Instance.GetKingTowerLevel());
            }
            SubmitLocalKingLevelServerRpc(myLevel);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SubmitLocalKingLevelServerRpc(int level, ServerRpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        // Server's own submission
        if (sender == NetworkManager.ServerClientId)
        {
            serverReportedLevel = Mathf.Max(1, level);
        }
        else
        {
            clientReportedLevel = Mathf.Max(1, level);
        }

        // If at least one side is known, we can start sending to each client their opponent level.
        // Prefer both known; otherwise fall back to mirroring if necessary.
        TryApplyAndBroadcast();
    }

    private void TryApplyAndBroadcast()
    {
        if (!IsServer) return;

        int hostLevel = (serverReportedLevel > 0) ? serverReportedLevel : 1;
        int clientLevel = (clientReportedLevel > 0) ? clientReportedLevel : hostLevel; // mirror if unknown

        // Send opponent level to host (opponent is the client level)
        SetOpponentLevelClientRpc(clientLevel, NetworkManager.ServerClientId);
        // Send opponent level to each non-host client (opponent is the host level)
        foreach (var kvp in NetworkManager.ConnectedClientsList)
        {
            ulong cid = kvp.ClientId;
            if (cid == NetworkManager.ServerClientId) continue;
            SetOpponentLevelClientRpc(hostLevel, cid);
        }

        // For safety, also apply on server now (so host sees correct values immediately)
        KingTower.SetOpponentKingLevel(clientLevel);
        KingTower.RecomputeAllKingsFromKnownLevels();
        applied = true;
    }

    [ClientRpc]
    private void SetOpponentLevelClientRpc(int opponentLevel, ulong targetClientId)
    {
        if (NetworkManager.Singleton == null) return;
        if (NetworkManager.Singleton.LocalClientId != targetClientId) return;

        KingTower.SetOpponentKingLevel(Mathf.Max(1, opponentLevel));
        KingTower.RecomputeAllKingsFromKnownLevels();
        Debug.Log($"[KingTowerLevelSync] Applied opponent level {opponentLevel} on client {targetClientId}");
    }
}

using UnityEngine;

/// <summary>
/// Stores ownership metadata (username + general owner tag).
/// Attach to any spawned object that needs to remember who created it.
/// </summary>
public class OwnerInfo : MonoBehaviour
{
    [Tooltip("Human username / player id who owns this object")]
    public string username;

    [Tooltip("General tag used for gameplay (e.g. 'Player' or 'Enemy')")]
    public string ownerTag;
}


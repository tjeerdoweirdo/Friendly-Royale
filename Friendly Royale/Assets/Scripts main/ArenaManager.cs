using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class ArenaManager : MonoBehaviour
{
    public static ArenaManager Instance { get; private set; }

    [Tooltip("All arena ScriptableObjects in ascending progression order.")]
    public List<Arena> arenas = new List<Arena>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        // Ensure at least the first arena is unlocked by default
        if (arenas != null && arenas.Count > 0)
        {
            if (!PlayerProgress.Instance.IsArenaUnlocked(arenas[0].arenaID))
                PlayerProgress.Instance.UnlockArena(arenas[0].arenaID);
        }
    }

    public List<Arena> GetAllArenas() => arenas;

    public List<Arena> GetUnlockedArenas()
    {
        var unlockedIDs = PlayerProgress.Instance.GetUnlockedArenaIDs();
        return arenas.Where(a => unlockedIDs.Contains(a.arenaID)).ToList();
    }

    public Arena GetArenaByID(string id)
    {
        return arenas.Find(a => a.arenaID == id);
    }

    public bool CanUnlockByTrophies(Arena arena)
    {
        return PlayerProgress.Instance.trophies >= arena.trophyRequirement;
    }

    public bool TryUnlockArenaByTrophies(Arena arena)
    {
        if (PlayerProgress.Instance.IsArenaUnlocked(arena.arenaID)) return true;
        if (PlayerProgress.Instance.trophies >= arena.trophyRequirement)
        {
            PlayerProgress.Instance.UnlockArena(arena.arenaID);
            return true;
        }
        return false;
    }
}

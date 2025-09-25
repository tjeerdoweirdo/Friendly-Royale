using UnityEngine;
using System.Collections.Generic;
using System.Linq;

[System.Serializable]
public class CardLevelEntry
{
    public string cardID;
    public int level;
}

[System.Serializable]
public class CardLevelCollection
{
    public List<CardLevelEntry> entries = new List<CardLevelEntry>();
}

[System.Serializable]
public class CardShardEntry
{
    public string cardID;
    public int shards;
}

[System.Serializable]
public class CardShardCollection
{
    public List<CardShardEntry> entries = new List<CardShardEntry>();
}

public class PlayerProgress : MonoBehaviour
{
    public event System.Action<int> OnGoldChanged;
    public event System.Action<int> OnTrophiesChanged;
    public static PlayerProgress Instance { get; private set; }

    [Header("Starting values")]
    public int startingGold = 500;
    public int startingTrophies = 0;

    [Header("Account")]
    [Tooltip("Default username shown in inspector if no saved username exists")]
    public string startingUsername = "Player";

    [Header("Runtime (persistent)")]
    public int gold = 0;
    public int trophies = 0;

    // persisted username (runtime)
    [Tooltip("Current player's username (persisted)")]
    public string username = "";

    private const string KEY_GOLD = "PP_GOLD_v2";
    private const string KEY_TROPHIES = "PP_TROPHIES_v2";
    private const string KEY_UNLOCKED_ARENAS = "PP_UNLOCKED_ARENAS_v2";
    private const string KEY_SELECTED_DECK_PREFIX = "PP_SELECTED_DECK_v2_";
    private const string KEY_CARDLEVEL_PREFIX = "PP_CARDLEVELS_v2_"; // + arenaID
    private const string KEY_CARDSHARD_PREFIX = "PP_CARDSHARDS_v2_"; // + arenaID
    private const string KEY_UNLOCKED_CARDS = "PP_UNLOCKED_CARDS_v2"; // global unlocked card list (from chests/shop)

    // username key
    private const string KEY_USERNAME = "PP_USERNAME_v1";

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        LoadProgress();
    }



        public int GetGold()
        {
            return gold;
        }

// ...existing code...
    void LoadProgress()
    {
        gold = PlayerPrefs.GetInt(KEY_GOLD, startingGold);
        trophies = PlayerPrefs.GetInt(KEY_TROPHIES, startingTrophies);

        // load username (fallback to startingUsername)
        username = PlayerPrefs.GetString(KEY_USERNAME, startingUsername ?? "Player");

        if (!PlayerPrefs.HasKey(KEY_UNLOCKED_ARENAS))
        {
            PlayerPrefs.SetString(KEY_UNLOCKED_ARENAS, "");
        }
        if (!PlayerPrefs.HasKey(KEY_UNLOCKED_CARDS))
        {
            PlayerPrefs.SetString(KEY_UNLOCKED_CARDS, "");
        }
    }

    void SaveProgress()
    {
        PlayerPrefs.SetInt(KEY_GOLD, gold);
        PlayerPrefs.SetInt(KEY_TROPHIES, trophies);

        // save username as well
        if (username == null) username = "";
        PlayerPrefs.SetString(KEY_USERNAME, username);

        PlayerPrefs.Save();
    }

    #region Username helpers
    /// <summary>
    /// Set and persist username immediately.
    /// </summary>
    public void SetUsername(string newUsername)
    {
        if (string.IsNullOrEmpty(newUsername))
        {
            username = "";
        }
        else
        {
            username = newUsername;
        }
        SaveProgress();
    }

    /// <summary>
    /// Get current username (may be empty string).
    /// </summary>
    public string GetUsername()
    {
        return username;
    }

    /// <summary>
    /// Clears stored username and persists the change.
    /// </summary>
    public void ClearUsername()
    {
        username = "";
        SaveProgress();
    }
    #endregion

    #region Gold / Trophies
    public bool SpendGold(int amount)
    {
    if (gold < amount) return false;
    gold -= amount;
    SaveProgress();
    OnGoldChanged?.Invoke(gold);
    return true;
    }

    public void AddGold(int amount)
    {
    gold += amount;
    SaveProgress();
    OnGoldChanged?.Invoke(gold);
    }

    public void AddTrophies(int amount)
    {
        trophies += amount;
        SaveProgress();
        OnTrophiesChanged?.Invoke(trophies);
    }
    #endregion

    #region Arena unlocks
    public List<string> GetUnlockedArenaIDs()
    {
        string s = PlayerPrefs.GetString(KEY_UNLOCKED_ARENAS, "");
        if (string.IsNullOrEmpty(s)) return new List<string>();
        return s.Split(',').Where(x => !string.IsNullOrEmpty(x)).ToList();
    }

    public void UnlockArena(string arenaID)
    {
        var list = GetUnlockedArenaIDs();
        if (!list.Contains(arenaID))
        {
            list.Add(arenaID);
            PlayerPrefs.SetString(KEY_UNLOCKED_ARENAS, string.Join(",", list));
            PlayerPrefs.Save();
        }
    }

    public bool IsArenaUnlocked(string arenaID)
    {
        var list = GetUnlockedArenaIDs();
        return list.Contains(arenaID);
    }
    #endregion

    #region Card unlocks (global)
    public bool IsCardUnlocked(string cardID)
    {
        string s = PlayerPrefs.GetString(KEY_UNLOCKED_CARDS, "");
        if (string.IsNullOrEmpty(s)) return false;
        return s.Split(',').Contains(cardID);
    }

    public void UnlockCard(string cardID)
    {
        if (IsCardUnlocked(cardID)) return;
        string s = PlayerPrefs.GetString(KEY_UNLOCKED_CARDS, "");
        var list = new List<string>();
        if (!string.IsNullOrEmpty(s)) list = s.Split(',').Where(x => !string.IsNullOrEmpty(x)).ToList();
        list.Add(cardID);
        PlayerPrefs.SetString(KEY_UNLOCKED_CARDS, string.Join(",", list));
        PlayerPrefs.Save();
    }
    #endregion

    #region Card levels per arena (persisted)
    public int GetCardLevel(string cardID, string arenaID)
    {
        CardLevelCollection col = LoadCardLevelCollection(arenaID);
        var e = col.entries.Find(x => x.cardID == cardID);
        return e != null ? Mathf.Max(1, e.level) : 1; // default level 1
    }

    public void SetCardLevel(string cardID, string arenaID, int level)
    {
        CardLevelCollection col = LoadCardLevelCollection(arenaID);
        var e = col.entries.Find(x => x.cardID == cardID);
        if (e == null)
        {
            e = new CardLevelEntry { cardID = cardID, level = level };
            col.entries.Add(e);
        }
        else e.level = level;
        SaveCardLevelCollection(arenaID, col);
    }

    public void IncreaseCardLevel(string cardID, string arenaID, int by = 1)
    {
        int cur = GetCardLevel(cardID, arenaID);
        SetCardLevel(cardID, arenaID, cur + by);
    }

    CardLevelCollection LoadCardLevelCollection(string arenaID)
    {
        string key = KEY_CARDLEVEL_PREFIX + arenaID;
        string json = PlayerPrefs.GetString(key, "");
        if (string.IsNullOrEmpty(json)) return new CardLevelCollection();
        return JsonUtility.FromJson<CardLevelCollection>(json) ?? new CardLevelCollection();
    }

    void SaveCardLevelCollection(string arenaID, CardLevelCollection col)
    {
        string key = KEY_CARDLEVEL_PREFIX + arenaID;
        string json = JsonUtility.ToJson(col);
        PlayerPrefs.SetString(key, json);
        PlayerPrefs.Save();
    }
    #endregion

    #region Card shards per arena (used by chests)
    public int GetCardShards(string cardID, string arenaID)
    {
        CardShardCollection col = LoadCardShardCollection(arenaID);
        var e = col.entries.Find(x => x.cardID == cardID);
        return e != null ? e.shards : 0;
    }

    public void AddCardShards(string cardID, string arenaID, int shardsToAdd)
    {
        if (shardsToAdd <= 0) return;
        CardShardCollection col = LoadCardShardCollection(arenaID);
        var e = col.entries.Find(x => x.cardID == cardID);
        if (e == null)
        {
            e = new CardShardEntry { cardID = cardID, shards = shardsToAdd };
            col.entries.Add(e);
        }
        else e.shards += shardsToAdd;

        SaveCardShardCollection(arenaID, col);

        // Try level up automatically: rule = shardsNeeded = 10 * currentLevel (simple)
        TryLevelUpFromShards(cardID, arenaID);
    }

    void TryLevelUpFromShards(string cardID, string arenaID)
    {
        bool leveled = true;
        while (leveled)
        {
            leveled = false;
            int curLevel = GetCardLevel(cardID, arenaID);
            int shards = GetCardShards(cardID, arenaID);
            int needed = Mathf.Max(1, 10 * curLevel); // e.g., level1->10 shards, level2->20 shards
            if (shards >= needed)
            {
                // consume shards and increase level
                SubtractCardShards(cardID, arenaID, needed);
                SetCardLevel(cardID, arenaID, curLevel + 1);
                leveled = true;
            }
        }
    }

    public void SubtractCardShards(string cardID, string arenaID, int shardsToRemove)
    {
        if (shardsToRemove <= 0) return;
        CardShardCollection col = LoadCardShardCollection(arenaID);
        var e = col.entries.Find(x => x.cardID == cardID);
        if (e == null) return;
        e.shards = Mathf.Max(0, e.shards - shardsToRemove);
        SaveCardShardCollection(arenaID, col);
    }

    CardShardCollection LoadCardShardCollection(string arenaID)
    {
        string key = KEY_CARDSHARD_PREFIX + arenaID;
        string json = PlayerPrefs.GetString(key, "");
        if (string.IsNullOrEmpty(json)) return new CardShardCollection();
        return JsonUtility.FromJson<CardShardCollection>(json) ?? new CardShardCollection();
    }

    void SaveCardShardCollection(string arenaID, CardShardCollection col)
    {
        string key = KEY_CARDSHARD_PREFIX + arenaID;
        string json = JsonUtility.ToJson(col);
        PlayerPrefs.SetString(key, json);
        PlayerPrefs.Save();
    }
    #endregion

    #region Selected deck per arena (persist)
    public void SaveSelectedDeckForArena(string arenaID, List<string> cardIDs)
    {
        string key = KEY_SELECTED_DECK_PREFIX + arenaID;
        string val = string.Join(",", cardIDs);
        PlayerPrefs.SetString(key, val);
        PlayerPrefs.Save();
    }

    public List<string> LoadSelectedDeckForArena(string arenaID)
    {
        string key = KEY_SELECTED_DECK_PREFIX + arenaID;
        string val = PlayerPrefs.GetString(key, "");
        if (string.IsNullOrEmpty(val)) return new List<string>();
        return val.Split(',').Where(x => !string.IsNullOrEmpty(x)).ToList();
    }
    #endregion
}

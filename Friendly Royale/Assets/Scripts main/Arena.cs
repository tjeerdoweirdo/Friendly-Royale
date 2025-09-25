using UnityEngine;

[CreateAssetMenu(menuName = "CR/Arena")]
public class Arena : ScriptableObject
{
    public string arenaID; // unique id, e.g., "arena_1"
    public string displayName;
    public Sprite preview;
    public int trophyRequirement = 0; // trophies needed to unlock
    public string sceneName; // scene to load for battles in this arena
    public int rewardTrophiesOnWin = 10;
    public int rewardGoldOnWin = 50;
}

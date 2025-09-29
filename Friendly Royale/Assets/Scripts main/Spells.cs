using UnityEngine;

public abstract class Spell : ScriptableObject
{
    public string spellName;
    public Sprite icon;
    public int coinCost = 1;
    public float range = 5f;
    public float duration = 2f;
    public abstract void Cast(Vector3 position, Unit.Faction casterFaction);
}

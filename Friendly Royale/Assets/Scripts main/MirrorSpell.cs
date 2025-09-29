using UnityEngine;

[CreateAssetMenu(menuName = "CR/Spell/Mirror")]
public class MirrorSpell : Spell
{
    public GameObject mirrorPrefab;
    
    public override void Cast(Vector3 position, Unit.Faction casterFaction)
    {
        // Spawn a mirrored unit (could be last played card, etc.)
        GameObject.Instantiate(mirrorPrefab, position, Quaternion.identity);
    }
}
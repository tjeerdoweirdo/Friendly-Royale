using UnityEngine;

[CreateAssetMenu(menuName = "CR/Spell/Clone")]
public class CloneSpell : Spell
{
    public GameObject clonePrefab;
    
    public override void Cast(Vector3 position, Unit.Faction casterFaction)
    {
        // Spawn a clone at the position
        GameObject.Instantiate(clonePrefab, position, Quaternion.identity);
    }
}
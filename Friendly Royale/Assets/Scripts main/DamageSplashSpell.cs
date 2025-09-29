using UnityEngine;

[CreateAssetMenu(menuName = "CR/Spell/DamageSplash")]
public class DamageSplashSpell : Spell
{
    public float damage = 50f;
    public float splashRadius = 3f;
    
    public override void Cast(Vector3 position, Unit.Faction casterFaction)
    {
        // Find all units in splashRadius and apply damage
        var hits = Physics.OverlapSphere(position, splashRadius);
        foreach (var hit in hits)
        {
            var unit = hit.GetComponent<Unit>();
            var health = hit.GetComponent<UnitHealth>();
            if (unit != null && health != null && unit.faction != casterFaction)
            {
                health.TakeDamage(Mathf.RoundToInt(damage));
            }
        }
    }
}
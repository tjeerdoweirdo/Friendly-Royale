using UnityEngine;

[CreateAssetMenu(menuName = "CR/Spell/Poison")]
public class PoisonSpell : Spell
{
    public float poisonDamagePerSecond = 10f;
    public float poisonRadius = 3f;
    
    public override void Cast(Vector3 position, Unit.Faction casterFaction)
    {
        var hits = Physics.OverlapSphere(position, poisonRadius);
        foreach (var hit in hits)
        {
            var unit = hit.GetComponent<Unit>();
            var health = hit.GetComponent<UnitHealth>();
            if (unit != null && health != null && unit.faction != casterFaction)
            {
                // Poison: damage over time. You may need to implement this in UnitHealth.
                health.StartCoroutine(ApplyPoisonCoroutine(health, poisonDamagePerSecond, duration));
            }
        }
    }

    private System.Collections.IEnumerator ApplyPoisonCoroutine(UnitHealth health, float dps, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration && health != null && health.IsAlive)
        {
            health.TakeDamage(Mathf.RoundToInt(dps));
            yield return new WaitForSeconds(1f);
            elapsed += 1f;
        }
    }
}
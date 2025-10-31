using UnityEngine;
using TMPro;

/// <summary>
/// Displays the latest enemy placement info using NetworkCardPlacementSystem telemetry.
/// This replaces any old event-based wiring.
/// </summary>
public class EnemyPlacementNotifier : MonoBehaviour
{
	[Tooltip("Optional TMP text to display enemy placement info.")]
	public TextMeshProUGUI targetText;

	[Tooltip("Prefix to use before the placement string.")]
	public string labelPrefix = "Last enemy placement:";

	private double _lastSeenTime = -1.0;

	void Reset()
	{
		// Try to find a TMP on this object by default
		if (targetText == null)
		{
			targetText = GetComponent<TextMeshProUGUI>();
		}
	}

	void Update()
	{
		// Poll telemetry once per frame; update only when there's a new placement
		double t = NetworkCardPlacementSystem.LastEnemyPlacementTime;
		if (t <= 0 || t == _lastSeenTime)
			return;

		_lastSeenTime = t;
		if (targetText == null)
			return;

		var orig = NetworkCardPlacementSystem.LastEnemyPlacementOriginal;
		var mapped = NetworkCardPlacementSystem.LastEnemyPlacementMapped;
		string card = NetworkCardPlacementSystem.LastEnemyPlacementCardId ?? "<card>";

		if (orig.HasValue)
		{
			var o = orig.Value;
			var m = mapped ?? orig;
			targetText.text = $"{labelPrefix} {card} | orig ({o.x:F1}, {o.z:F1}) -> mapped ({m.Value.x:F1}, {m.Value.z:F1})";
		}
		else
		{
			targetText.text = $"{labelPrefix} <none>";
		}
	}
}


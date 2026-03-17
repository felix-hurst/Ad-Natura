using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class StructuralCollapseManager : MonoBehaviour
{
    private static StructuralCollapseManager instance;

    [Header("Successive Explosions")]
    [SerializeField] private int numberOfExplosionRounds = 6;
    [SerializeField] private float delayBetweenRounds = 0.6f;
    [SerializeField] private int startingMinRays = 5;
    [SerializeField] private int startingMaxRays = 10;
    [SerializeField] private float rayReductionPerRound = 0.7f;
    [SerializeField] private float explosionRayDistance = 5f;

    [Header("Anti-Sliver Settings")]
    [SerializeField] private float minAngleBetweenRays = 25f;
    [SerializeField] private float angleRandomness = 15f;

    [Header("Fragment Size Limits")]
    [SerializeField] private float minFragmentArea = 0.1f;
    [SerializeField] private bool validateFragmentSize = true;

    [Header("Fragment Cleanup (Defaults — overridden per call)")]
    [SerializeField] private bool destroyFragmentsAfterExplosion = true;
    [SerializeField] private float fragmentCleanupDelay = 3f;
    [SerializeField] private bool destroyParentAfterExplosion = true;
    [SerializeField] private float parentCleanupDelay = 0.5f;

    public static StructuralCollapseManager Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject go = new GameObject("StructuralCollapseManager");
                instance = go.AddComponent<StructuralCollapseManager>();
                DontDestroyOnLoad(go);
            }
            return instance;
        }
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
    }

    /// <summary>
    /// Schedule a delayed multi-round explosion with full control over cleanup.
    /// </summary>
    public void ScheduleDelayedExplosion(
        GameObject target,
        Vector2 explosionOrigin,
        float delay,
        int minRayCount,
        int maxRayCount,
        float rayDistance,
        float minAngle,
        float maxAngle,
        bool showRays,
        float rayVisualizationDuration,
        Color rayColor,
        bool showWarning,
        float warningDuration,
        Color warningColor,
        bool destroyParent = true,
        float parentDestroyDelay = 0.5f,
        bool destroyFragments = true,
        float fragmentDestroyDelay = 3f)
    {
        StartCoroutine(DelayedExplosionCoroutine(
            target,
            explosionOrigin,
            delay,
            minRayCount,
            maxRayCount,
            rayDistance,
            minAngle,
            maxAngle,
            showRays,
            rayVisualizationDuration,
            rayColor,
            showWarning,
            warningDuration,
            warningColor,
            destroyParent,
            parentDestroyDelay,
            destroyFragments,
            fragmentDestroyDelay));
    }

    IEnumerator DelayedExplosionCoroutine(
        GameObject target,
        Vector2 explosionOrigin,
        float delay,
        int minRayCount,
        int maxRayCount,
        float rayDistance,
        float minAngle,
        float maxAngle,
        bool showRays,
        float rayVisualizationDuration,
        Color rayColor,
        bool showWarning,
        float warningDuration,
        Color warningColor,
        bool destroyParent,
        float parentDestroyDelay,
        bool destroyFragments,
        float fragmentDestroyDelay)
    {
        if (target == null) yield break;

        string materialTag = target.tag;
        if (string.IsNullOrEmpty(materialTag) || materialTag == "Untagged")
            materialTag = target.name;

        ExplosionFragment marker = target.GetComponent<ExplosionFragment>();
        if (marker == null)
            marker = target.AddComponent<ExplosionFragment>();
        marker.Initialize(materialTag);

        // --- Warning visualization ---
        GameObject warningVis = null;
        if (showWarning && warningDuration > 0)
        {
            float warningStart = delay - warningDuration;
            if (warningStart > 0)
            {
                yield return new WaitForSeconds(warningStart);
                if (target != null)
                    warningVis = CreateWarningVisualization(target, warningColor, warningDuration);
                yield return new WaitForSeconds(warningDuration);
            }
            else
            {
                yield return new WaitForSeconds(delay);
            }
        }
        else
        {
            yield return new WaitForSeconds(delay);
        }

if (warningVis != null) Destroy(warningVis);
if (target == null) yield break;

// Move parent to CutPiece layer before any cuts begin
int cutPieceLayer = LayerMask.NameToLayer("CutPiece");
if (cutPieceLayer != -1)
    target.layer = cutPieceLayer;

ExplosionFragment targetFragment = target.GetComponent<ExplosionFragment>();
        if (targetFragment == null)
        {
            Debug.LogWarning($"[StructuralCollapseManager] Target {target.name} has no ExplosionFragment, aborting");
            yield break;
        }

        // --- Multi-round cutting ---
        int currentMinRays = startingMinRays;
        int currentMaxRays = startingMaxRays;

        // Use the per-call ray counts for round 1, then scale down
        int callMinRays = minRayCount;
        int callMaxRays = maxRayCount;

        for (int round = 1; round <= numberOfExplosionRounds; round++)
        {
            if (targetFragment == null || targetFragment.gameObject == null)
                break;

            GameObject fragmentObj = targetFragment.gameObject;
            Bounds fragmentBounds = GetObjectBounds(fragmentObj);
            Vector2 fragmentCenter = fragmentBounds.center;

            // Round 1 uses caller-specified ray counts, subsequent rounds use manager defaults scaled down
            int roundMin = round == 1 ? callMinRays : currentMinRays;
            int roundMax = round == 1 ? callMaxRays : currentMaxRays;

            int rayCount = Random.Range(roundMin, roundMax + 1);
            List<float> angles = GenerateRandomAngles(rayCount, minAngle, maxAngle);

            Debug.Log($"[SCM] Round {round}/{numberOfExplosionRounds}: {rayCount} rays on '{fragmentObj.name}'");

            int totalCuts = 0;
            foreach (float angle in angles)
            {
                Vector2 direction = new Vector2(
                    Mathf.Cos(angle * Mathf.Deg2Rad),
                    Mathf.Sin(angle * Mathf.Deg2Rad));

                Color roundColor = round == 1
                    ? rayColor
                    : Color.Lerp(rayColor, Color.red, (round - 1) / (float)numberOfExplosionRounds);

                bool cutMade = PerformExplosionRaycast(
                    fragmentObj, fragmentCenter, direction, explosionRayDistance,
                    showRays, rayVisualizationDuration, roundColor);

                if (cutMade)
                {
                    totalCuts++;
                    yield return new WaitForFixedUpdate();
                    yield return new WaitForFixedUpdate();
                    yield return new WaitForFixedUpdate();
                }
            }

            Debug.Log($"[SCM] Round {round} complete: {totalCuts} cuts made");

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Scale down rays each round
            currentMinRays = Mathf.Max(1, Mathf.RoundToInt(currentMinRays * rayReductionPerRound));
            currentMaxRays = Mathf.Max(2, Mathf.RoundToInt(currentMaxRays * rayReductionPerRound));

            if (round < numberOfExplosionRounds)
                yield return new WaitForSeconds(delayBetweenRounds);
        }

        // --- Cleanup ---
        if (destroyParent && target != null)
        {
            yield return new WaitForSeconds(parentDestroyDelay);
            if (target != null)
            {
                Debug.Log($"[SCM] Destroying parent '{target.name}' after explosion");
                Destroy(target);
            }
        }

        if (destroyFragments)
        {
            yield return new WaitForSeconds(fragmentDestroyDelay);
            DestroyAllFragments();
        }
    }

    public void ScheduleSingleRoundExplosion(
        GameObject target,
        Vector2 explosionOrigin,
        float delay,
        int minRayCount,
        int maxRayCount,
        float rayDistance,
        float minAngle,
        float maxAngle,
        bool showRays,
        float rayVisualizationDuration,
        Color rayColor,
        bool showWarning,
        float warningDuration,
        Color warningColor,
        bool destroyParent = true,
        float parentDestroyDelay = 0.5f,
        bool destroyFragments = true,
        float fragmentDestroyDelay = 3f)
    {
        StartCoroutine(SingleRoundExplosionCoroutine(
            target,
            explosionOrigin,
            delay,
            minRayCount,
            maxRayCount,
            rayDistance,
            minAngle,
            maxAngle,
            showRays,
            rayVisualizationDuration,
            rayColor,
            showWarning,
            warningDuration,
            warningColor,
            destroyParent,
            parentDestroyDelay,
            destroyFragments,
            fragmentDestroyDelay));
    }

    IEnumerator SingleRoundExplosionCoroutine(
        GameObject target,
        Vector2 explosionOrigin,
        float delay,
        int minRayCount,
        int maxRayCount,
        float rayDistance,
        float minAngle,
        float maxAngle,
        bool showRays,
        float rayVisualizationDuration,
        Color rayColor,
        bool showWarning,
        float warningDuration,
        Color warningColor,
        bool destroyParent,
        float parentDestroyDelay,
        bool destroyFragments,
        float fragmentDestroyDelay)
    {
        if (target == null) yield break;

        string materialTag = target.tag;
        if (string.IsNullOrEmpty(materialTag) || materialTag == "Untagged")
            materialTag = target.name;

        ExplosionFragment marker = target.GetComponent<ExplosionFragment>();
        if (marker == null)
            marker = target.AddComponent<ExplosionFragment>();
        marker.Initialize(materialTag);

        GameObject warningVis = null;
        if (showWarning && warningDuration > 0)
        {
            float warningStart = delay - warningDuration;
            if (warningStart > 0)
            {
                yield return new WaitForSeconds(warningStart);
                if (target != null)
                    warningVis = CreateWarningVisualization(target, warningColor, warningDuration);
                yield return new WaitForSeconds(warningDuration);
            }
            else
            {
                yield return new WaitForSeconds(delay);
            }
        }
        else
        {
            yield return new WaitForSeconds(delay);
        }

if (warningVis != null) Destroy(warningVis);
if (target == null) yield break;

// Move parent to CutPiece layer before any cuts begin
int cutPieceLayer = LayerMask.NameToLayer("CutPiece");
if (cutPieceLayer != -1)
    target.layer = cutPieceLayer;

ExplosionFragment[] fragments = FindObjectsOfType<ExplosionFragment>();

        foreach (ExplosionFragment fragment in fragments)
        {
            if (fragment == null || fragment.gameObject == null) continue;

            GameObject fragmentObj = fragment.gameObject;
            Bounds fragmentBounds = GetObjectBounds(fragmentObj);
            Vector2 fragmentCenter = fragmentBounds.center;

            int rayCount = Random.Range(minRayCount, maxRayCount + 1);
            List<float> angles = GenerateRandomAngles(rayCount, minAngle, maxAngle);

            foreach (float angle in angles)
            {
                Vector2 direction = new Vector2(
                    Mathf.Cos(angle * Mathf.Deg2Rad),
                    Mathf.Sin(angle * Mathf.Deg2Rad));

                bool cutMade = PerformExplosionRaycast(
                    fragmentObj, fragmentCenter, direction, rayDistance,
                    showRays, rayVisualizationDuration, rayColor);

                if (cutMade)
                {
                    yield return new WaitForFixedUpdate();
                    yield return new WaitForFixedUpdate();
                }
            }

            yield return new WaitForFixedUpdate();
        }

        yield return new WaitForFixedUpdate();

        if (destroyParent && target != null)
        {
            yield return new WaitForSeconds(parentDestroyDelay);
            if (target != null) Destroy(target);
        }

        if (destroyFragments)
        {
            yield return new WaitForSeconds(fragmentDestroyDelay);
            DestroyAllFragments();
        }
    }

    void DestroyAllFragments()
    {
        ExplosionFragment[] allFragments = FindObjectsOfType<ExplosionFragment>();
        foreach (ExplosionFragment fragment in allFragments)
        {
            if (fragment != null && fragment.gameObject != null)
                Destroy(fragment.gameObject);
        }
    }

    GameObject CreateWarningVisualization(GameObject target, Color warningColor, float warningDuration)
    {
        Bounds bounds = GetObjectBounds(target);
        if (bounds.size.x < 0.1f || bounds.size.y < 0.1f) return null;

        GameObject warningObj = new GameObject($"{target.name}_Warning");
        LineRenderer lineRenderer = warningObj.AddComponent<LineRenderer>();

        lineRenderer.startWidth = 0.15f;
        lineRenderer.endWidth = 0.15f;
        lineRenderer.material = new Material(Shader.Find("Unlit/Color"));
        lineRenderer.material.color = warningColor;
        lineRenderer.startColor = warningColor;
        lineRenderer.endColor = warningColor;
        lineRenderer.sortingOrder = 20;
        lineRenderer.useWorldSpace = true;
        lineRenderer.loop = true;

        StartCoroutine(PulseWarning(lineRenderer, warningColor, warningDuration));

        Vector3[] corners = new Vector3[4];
        corners[0] = new Vector3(bounds.min.x, bounds.min.y, 0);
        corners[1] = new Vector3(bounds.max.x, bounds.min.y, 0);
        corners[2] = new Vector3(bounds.max.x, bounds.max.y, 0);
        corners[3] = new Vector3(bounds.min.x, bounds.max.y, 0);

        lineRenderer.positionCount = 4;
        lineRenderer.SetPositions(corners);

        return warningObj;
    }

    IEnumerator PulseWarning(LineRenderer lineRenderer, Color warningColor, float warningDuration)
    {
        float elapsed = 0f;
        while (lineRenderer != null && elapsed < warningDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.PingPong(elapsed * 4f, 1f);
            Color pulseColor = warningColor;
            pulseColor.a = Mathf.Lerp(0.3f, 1f, alpha);
            lineRenderer.startColor = pulseColor;
            lineRenderer.endColor = pulseColor;
            yield return null;
        }
    }

    List<float> GenerateRandomAngles(int count, float minAngle, float maxAngle)
    {
        List<float> angles = new List<float>();
        if (count <= 0) return angles;

        float angleRange = maxAngle - minAngle;
        float sectorSize = angleRange / count;

        for (int i = 0; i < count; i++)
        {
            float sectorStart = minAngle + sectorSize * i;
            float sectorEnd = sectorStart + sectorSize;
            float margin = sectorSize * 0.15f;
            angles.Add(Random.Range(sectorStart + margin, sectorEnd - margin));
        }

        return angles;
    }

    List<float> EnsureMinimumAngleSeparation(List<float> angles, float minSeparation)
    {
        if (angles.Count <= 1) return angles;
        angles.Sort();

        List<float> validAngles = new List<float> { angles[0] };
        for (int i = 1; i < angles.Count; i++)
        {
            if (angles[i] - validAngles[validAngles.Count - 1] >= minSeparation)
                validAngles.Add(angles[i]);
        }

        return validAngles;
    }

    bool PerformExplosionRaycast(GameObject target, Vector2 origin, Vector2 direction,
                                 float rayDistance, bool showRays, float rayVisualizationDuration,
                                 Color rayColor)
    {
        RaycastHit2D[] hits = Physics2D.RaycastAll(origin, direction, rayDistance);

        if (showRays)
            Debug.DrawRay(origin, direction * rayDistance, rayColor, rayVisualizationDuration);

        RaycastHit2D targetHit = default;
        bool foundTarget = false;

        foreach (RaycastHit2D hit in hits)
        {
            if (hit.collider.gameObject == target)
            {
                targetHit = hit;
                foundTarget = true;
                break;
            }
        }

        if (!foundTarget) return false;

        Vector2 entryPoint = targetHit.point;
        Collider2D targetCollider = targetHit.collider;

        Bounds bounds = targetCollider.bounds;
        Vector2 farPoint = origin + direction * (rayDistance + bounds.size.magnitude);
        RaycastHit2D[] reverseHits = Physics2D.RaycastAll(farPoint, -direction, rayDistance + bounds.size.magnitude);

        Vector2 exitPoint = Vector2.zero;
        bool foundExit = false;

        foreach (RaycastHit2D hit in reverseHits)
        {
            if (hit.collider == targetCollider)
            {
                exitPoint = hit.point;
                foundExit = true;
                break;
            }
        }

        if (foundExit && Vector2.Distance(entryPoint, exitPoint) > 0.1f)
        {
            RaycastReceiver receiver = target.GetComponent<RaycastReceiver>();
            if (receiver != null)
            {
                if (showRays)
                {
                    DrawExplosionCutPoint(entryPoint, Color.green, rayVisualizationDuration);
                    DrawExplosionCutPoint(exitPoint, Color.red, rayVisualizationDuration);
                }

                receiver.ExecuteCutDirect(entryPoint, exitPoint, null);
                return true;
            }
            else
            {
                Debug.LogWarning($"[SCM] No RaycastReceiver on '{target.name}' — cut skipped");
            }
        }

        return false;
    }

    Bounds GetObjectBounds(GameObject obj)
    {
        Renderer renderer = obj.GetComponent<Renderer>();
        if (renderer != null) return renderer.bounds;

        renderer = obj.GetComponentInChildren<Renderer>();
        if (renderer != null) return renderer.bounds;

        Collider2D collider = obj.GetComponent<Collider2D>();
        if (collider != null) return collider.bounds;

        return new Bounds(obj.transform.position, Vector3.one);
    }

    void DrawExplosionCutPoint(Vector2 point, Color color, float duration)
    {
        Debug.DrawLine(new Vector3(point.x - 0.1f, point.y - 0.1f, 0),
                       new Vector3(point.x + 0.1f, point.y + 0.1f, 0), color, duration);
        Debug.DrawLine(new Vector3(point.x - 0.1f, point.y + 0.1f, 0),
                       new Vector3(point.x + 0.1f, point.y - 0.1f, 0), color, duration);
    }

    public class ExplosionFragment : MonoBehaviour
    {
        public string materialType;
        public float explosionTime;

        public void Initialize(string material)
        {
            materialType = material;
            explosionTime = Time.time;
        }
    }
}
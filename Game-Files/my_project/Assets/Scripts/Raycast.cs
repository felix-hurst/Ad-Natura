using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class Raycast : MonoBehaviour
{
    [Header("Raycast Settings")]
    [SerializeField] private float dotSize = 0.2f;
    [SerializeField] private float dashWorldSize = 0.2f;

    [Header("Arc Settings")]
    [SerializeField] private int arcResolution = 60;

    private LineRenderer lineRenderer;
    private GameObject entryDot;
    private GameObject exitDot;
    private Transform playerTransform;
    private Transform muzzleTransform;
    private PlayerController.ToolType currentTool;
    private GameObject projectilePrefab;
    private float throwForce = 15f;
    private float ballSpawnOffset = 1.0f;
    private int arcMask;

    [Header("Muzzle Blast Settings")]
    [SerializeField] private BurstLeafSystem.MuzzleBlastSettings muzzleBlastSettings = new BurstLeafSystem.MuzzleBlastSettings();

    public void Initialize(Transform player, Transform muzzle, Texture2D dashTexture)
    {
        playerTransform = player;
        muzzleTransform = muzzle;

        lineRenderer = gameObject.AddComponent<LineRenderer>();
        lineRenderer.startWidth = 0.1f;
        lineRenderer.endWidth = 0.1f;
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.material.mainTexture = dashTexture;
        lineRenderer.sortingOrder = 5;
        lineRenderer.useWorldSpace = true;
        lineRenderer.textureMode = LineTextureMode.Tile;

        // Create aiming dots
        entryDot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        entryDot.transform.localScale = Vector3.one * dotSize;
        Destroy(entryDot.GetComponent<Collider>());
        entryDot.SetActive(false);

        exitDot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        exitDot.transform.localScale = Vector3.one * dotSize;
        Destroy(exitDot.GetComponent<Collider>());
        exitDot.SetActive(false);

        arcMask = ~((1 << LayerMask.NameToLayer("Player")) |
            (1 << LayerMask.NameToLayer("Ignore Raycast")) |
            (1 << LayerMask.NameToLayer("WaterCollider")) |
            (1 << LayerMask.NameToLayer("SlimeBoundary")) |
            (1 << LayerMask.NameToLayer("CutPiece")) |
            (1 << LayerMask.NameToLayer("SlimeObstacle")) |
            (1 << LayerMask.NameToLayer("Background")) |
            (1 << LayerMask.NameToLayer("UI")));
        SetLaserColor(Color.white);
    }

    private void SetLaserColor(Color color)
    {
        if (lineRenderer == null) return;
        lineRenderer.material.color = color;
        lineRenderer.startColor = color;
        lineRenderer.endColor = color;
    }

    // This matches the call from your updated PlayerController
    public void SetCurrentTool(PlayerController.ToolType tool, GameObject ballPrefab, float force, float spawnOffset, float range)
    {
        currentTool = tool;
        projectilePrefab = ballPrefab;
        throwForce = force;
        ballSpawnOffset = spawnOffset;
    }

    void Update()
    {
        if (muzzleTransform == null) return;

        Vector3 mousePos = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        mousePos.z = 0;
        Vector2 direction = ((Vector2)mousePos - (Vector2)muzzleTransform.position).normalized;

        // Water and Incendiary use Arcs. Wind uses a straight line.
        if (currentTool == PlayerController.ToolType.WaterBall || currentTool == PlayerController.ToolType.IncendiaryBall)
        {
            DrawArc(direction);
        }
        else
        {
            DrawStraightLine(mousePos);
        }

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            PlayerController pc = playerTransform.GetComponent<PlayerController>();
            if (pc != null && pc.RequestAmmoUse(currentTool))
            {
                ThrowProjectile(direction);
            }
        }
    }

    private void DrawStraightLine(Vector3 target)
    {
        lineRenderer.positionCount = 2;
        lineRenderer.SetPosition(0, muzzleTransform.position);
        lineRenderer.SetPosition(1, target);
        float dist = Vector3.Distance(muzzleTransform.position, target);
        lineRenderer.material.mainTextureScale = new Vector2(dist / dashWorldSize, 1f);
    }

    private void DrawArc(Vector2 direction)
    {
        Vector2 velocity = direction * throwForce;
        Vector2 startPos = (Vector2)muzzleTransform.position;
        List<Vector3> points = new List<Vector3>() { startPos };
        Vector2 lastPos = startPos;
        float totalLength = 0;

        for (int i = 1; i < arcResolution; i++)
        {
            float t = i * 0.05f;
            Vector2 pos = startPos + (velocity * t) + 0.5f * Physics2D.gravity * t * t;
            RaycastHit2D hit = Physics2D.Linecast(lastPos, pos, arcMask);

            Vector3 currentPoint = hit.collider != null ? (Vector3)hit.point : (Vector3)pos;
            totalLength += Vector3.Distance(lastPos, currentPoint);
            points.Add(currentPoint);

            if (hit.collider != null) break;
            lastPos = currentPoint;
        }

        lineRenderer.positionCount = points.Count;
        lineRenderer.SetPositions(points.ToArray());
        lineRenderer.material.mainTextureScale = new Vector2(totalLength / dashWorldSize, 1f);
    }

    void ThrowProjectile(Vector2 direction)
    {
        if (projectilePrefab == null) return;

        GameObject projectile = Instantiate(projectilePrefab, muzzleTransform.position, Quaternion.identity);
        Rigidbody2D rb = projectile.GetComponent<Rigidbody2D>();

        // Wind Ball ignores gravity to fly straight
        rb.gravityScale = (currentTool == PlayerController.ToolType.WindBall) ? 0f : 1f;
        rb.linearVelocity = direction * throwForce;

        if (currentTool == PlayerController.ToolType.IncendiaryBall)
            BurstLeafSystem.MuzzleBlastAll(muzzleTransform.position, direction, muzzleBlastSettings);
    }

    public void Cleanup()
    {
        if (entryDot) Destroy(entryDot);
        if (exitDot) Destroy(exitDot);
    }

    private void OnEnable()
    {
        if (lineRenderer != null)
        {
            lineRenderer.enabled = true;
        }
    }

    private void OnDisable()
    {
        if (lineRenderer != null)
        {
            lineRenderer.enabled = false;
        }
    }


}
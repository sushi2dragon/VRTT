using System.Collections;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class AutoserveCl : MonoBehaviour
{
    [Header("References")]
    public Transform ballSpawnParent;     // parent for spawned balls (optional)
    public GameObject ballPrefab;         // prefab containing Rigidbody & Collider
    public Collider tableCollider;

    [Header("Table & Net geometry (set to your scene values)")]
    public float tableTopY = 0.76f;
    public float netTopY = 0.928f;
    public float serveStartZMin = -1.89f;
    public float serveStartZMax = -1.75f;

    [Header("X Ranges for serving sides (local table space)")]
    public float leftXMin = -0.406f;
    public float leftXMax = 0.255f;
    public float rightXMin = 0.255f;
    public float rightXMax = 0.979f;

    [Header("Ball / Physics settings")]
    public float restitution = 0.8f;           // used for analytic prediction
    public float gravityY = -9.81f;
    public float startHeightAboveTable = 0.30f;
    public float minTimeToFirstBounce = 0.08f;
    public float maxTimeToFirstBounce = 0.8f;
    public float spawnedBallLifetime = 12f;   // seconds before cleaning up spawned balls

    [Header("Serve tuning")]
    public float firstBounceForwardOffset = 0.18f;
    public float firstBounceXOffsetRange = 0.05f;
    public int maxSolveAttempts = 200;

    [Header("Angle / inward bias (new)")]
    [Tooltip("When serving from one side, bias the first bounce X this fraction toward the table center (0..1).")]
    public float inwardBiasMin = 0.15f;
    public float inwardBiasMax = 0.45f;
    [Tooltip("Maximum additional lateral (x) velocity magnitude to try (m/s). Always points inward).")]
    public float maxExtraLateralVel = 0.6f;
    [Tooltip("How many lateral tweak attempts per analytic solution to try before rejecting.")]
    public int lateralTweakAttempts = 6;

    [Header("Spin & extras")]
    public Vector3 extraAngularVelocity = Vector3.zero;

    [Header("Serve randomness")]
    [Range(0f, 1f)]
    public float backwardServeChance = 0.15f;  // chance to flip forwardSign for a backward serve
    public float serveForceFallback = 2.8f;    // used by fallback serve

    int serveIndex = 0;
    bool servingLeft = true;
    AudioSource audioSrc;
    public AudioClip serveSound;

    void Awake()
    {
        audioSrc = GetComponent<AudioSource>();
        gravityY = Physics.gravity.y;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
            StartCoroutine(DoServeRoutine());
    }

    IEnumerator DoServeRoutine()
    {
        if (ballPrefab == null)
        {
            Debug.LogError("AutoserveCloned: ballPrefab not assigned.");
            yield break;
        }

        // table world center (used as offset)
        Vector3 tableCenter = tableCollider ? tableCollider.bounds.center : Vector3.zero;

        int sideGroup = (serveIndex / 2) % 2;
        servingLeft = (sideGroup == 0);

        // spawn position in local table coords then convert to world
        float spawnXLocal = servingLeft ? Random.Range(leftXMin, leftXMax) : Random.Range(rightXMin, rightXMax);
        float spawnZLocal = Random.Range(serveStartZMin, serveStartZMax);
        Vector3 spawnPos = new Vector3(spawnXLocal, tableTopY + startHeightAboveTable, spawnZLocal) + new Vector3(tableCenter.x, 0f, tableCenter.z);

        // Instantiate a clone for this serve
        GameObject go = Instantiate(ballPrefab, spawnPos, Quaternion.identity, ballSpawnParent);
        Rigidbody ballRb = go.GetComponent<Rigidbody>();
        if (ballRb == null)
        {
            Debug.LogError("AutoserveCloned: ballPrefab has no Rigidbody.");
            Destroy(go);
            yield break;
        }

        // ensure initial physical state
        ballRb.linearVelocity = Vector3.zero;
        ballRb.angularVelocity = Vector3.zero;
        ballRb.isKinematic = false;

        // forward sign toward table center by default
        float forwardSign = -1f;
        if (tableCollider != null)
        {
            forwardSign = Mathf.Sign(tableCenter.z - spawnPos.z);
            if (forwardSign == 0f) forwardSign = -1f;
        }

        // Occasionally flip forwardSign to create a "backwards" serve.
        if (Random.value < backwardServeChance)
        {
            forwardSign *= -1f;
        }

        // Pick first-bounce local X but biased inward toward table center:
        float bias = Random.Range(inwardBiasMin, inwardBiasMax); // fraction
        float targetSideInnerX = servingLeft ? rightXMin : leftXMax;
        float p1XLocalBase = Mathf.Lerp(spawnXLocal, targetSideInnerX, bias);
        float p1XLocal = Mathf.Clamp(p1XLocalBase + Random.Range(-firstBounceXOffsetRange, firstBounceXOffsetRange),
                                      servingLeft ? leftXMin : rightXMin,
                                      servingLeft ? leftXMax : rightXMax);

        Vector3 p1Local = new Vector3(p1XLocal, tableTopY, spawnZLocal + forwardSign * Mathf.Abs(firstBounceForwardOffset));
        Vector3 p1 = p1Local + new Vector3(tableCenter.x, 0f, tableCenter.z);

        bool solved = false;
        Vector3 solvedVelocity = Vector3.zero;

        // Try to analytically find an initial velocity that produces correct two bounces.
        for (int attempt = 0; attempt < maxSolveAttempts; attempt++)
        {
            // small jitter of p1 z and x for variety
            Vector3 candidateP1 = new Vector3(
                Mathf.Clamp(p1Local.x + Random.Range(-0.02f, 0.02f), servingLeft ? leftXMin : rightXMin, servingLeft ? leftXMax : rightXMax) + tableCenter.x,
                tableTopY,
                p1Local.z + Random.Range(-0.04f, 0.04f) + tableCenter.z
            );

            float t1 = Random.Range(minTimeToFirstBounce, maxTimeToFirstBounce);
            Vector3 v = SolveInitialVelocityForLandingAt(spawnPos, candidateP1, t1, gravityY);
            if (v == Vector3.zero) continue;

            // If v's lateral direction is wrong (not pointing inward), nudge candidateP1.x more inward and retry
            if (servingLeft && v.x < 0f)
            {
                p1Local.x = Mathf.Lerp(p1Local.x, targetSideInnerX, 0.12f);
                p1 = p1Local + new Vector3(tableCenter.x, 0f, tableCenter.z);
                continue;
            }
            if (!servingLeft && v.x > 0f)
            {
                p1Local.x = Mathf.Lerp(p1Local.x, targetSideInnerX, 0.12f);
                p1 = p1Local + new Vector3(tableCenter.x, 0f, tableCenter.z);
                continue;
            }

            // Now try small inward lateral tweaks (try a few magnitudes) � these add realism (slight angle)
            for (int lTry = 0; lTry < lateralTweakAttempts; lTry++)
            {
                float lateralMag = Random.Range(0f, maxExtraLateralVel);
                float lateralVel = servingLeft ? Mathf.Abs(lateralMag) : -Mathf.Abs(lateralMag);

                Vector3 vTweaked = new Vector3(v.x + lateralVel, v.y, v.z);

                // predict second bounce
                if (PredictSecondBounceLocation(spawnPos, vTweaked, candidateP1, out Vector3 secondBounce))
                {
                    float secondMinX = servingLeft ? (tableCenter.x + rightXMin) : (tableCenter.x + leftXMin);
                    float secondMaxX = servingLeft ? (tableCenter.x + rightXMax) : (tableCenter.x + leftXMax);
                    bool secondOnOpponentSide = (secondBounce.x >= secondMinX - 0.001f && secondBounce.x <= secondMaxX + 0.001f);

                    // net clearance
                    bool clearsNet = true;
                    if (tableCollider != null)
                    {
                        float netZ = tableCenter.z;
                        float timeToNet = TimeToReachZ(spawnPos.z, vTweaked.z, netZ - spawnPos.z);
                        if (timeToNet > 0f)
                        {
                            float yAtNet = spawnPos.y + vTweaked.y * timeToNet + 0.5f * gravityY * timeToNet * timeToNet;
                            if (yAtNet <= netTopY + 0.01f) clearsNet = false;
                        }
                    }

                    if (secondOnOpponentSide && clearsNet)
                    {
                        float pathStretch = Mathf.Abs(vTweaked.x) / Mathf.Max(0.01f, Mathf.Abs(vTweaked.z));
                        float verticalBoost = Mathf.Lerp(0.02f, 0.08f, Mathf.Clamp01(pathStretch * 1.5f));
                        vTweaked.y += verticalBoost;

                        // ensure arc height over net
                        float netZ = tableCenter.z;
                        float timeToNet = TimeToReachZ(spawnPos.z, vTweaked.z, netZ - spawnPos.z);
                        if (timeToNet > 0f)
                        {
                            float yAtNet = spawnPos.y + vTweaked.y * timeToNet + 0.5f * gravityY * timeToNet * timeToNet;
                            if (yAtNet < netTopY + 0.05f)
                            {
                                vTweaked.y += (netTopY + 0.07f - yAtNet) * 1.15f;
                            }
                        }

                        solved = true;
                        solvedVelocity = vTweaked;
                        break;
                    }
                }
            }

            if (solved) break;
        }

        if (!solved)
        {
            Debug.LogWarning("AutoserveCloned: no analytical serve found with angle, using fallback slower serve.");
            float fallbackLateral = servingLeft ? 0.25f : -0.25f;
            solvedVelocity = new Vector3(fallbackLateral, 1.1f, forwardSign * serveForceFallback);
        }

        // apply final velocity and spin to this clone
        ballRb.linearVelocity = solvedVelocity;
        ballRb.angularVelocity = extraAngularVelocity;

        if (serveSound && audioSrc) audioSrc.PlayOneShot(serveSound);

        // schedule cleanup for the spawned ball to avoid clutter
        Destroy(go, spawnedBallLifetime);

        serveIndex++;
        yield return null;
    }

    Vector3 SolveInitialVelocityForLandingAt(Vector3 p0, Vector3 pLanding, float t, float g)
    {
        if (t <= 0f) return Vector3.zero;
        Vector3 vxz = (new Vector3(pLanding.x, 0f, pLanding.z) - new Vector3(p0.x, 0f, p0.z)) / t;
        float vy = (pLanding.y - p0.y - 0.5f * g * t * t) / t;
        Vector3 v = new Vector3(vxz.x, vy, vxz.z);
        if (float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z)) return Vector3.zero;
        if (v.magnitude > 60f) return Vector3.zero;
        return v;
    }

    bool PredictSecondBounceLocation(Vector3 p0, Vector3 v0, Vector3 pBounce1, out Vector3 pBounce2)
    {
        pBounce2 = Vector3.zero;
        float t1 = TimeToReachY(p0.y, v0.y, tableTopY, gravityY);
        if (t1 <= 0f) return false;

        float vy_pre = v0.y + gravityY * t1;
        float vy_post = -restitution * vy_pre;
        Vector3 vHoriz = new Vector3(v0.x, 0f, v0.z);

        float g = gravityY;
        if (Mathf.Approximately(g, 0f)) return false;
        float t2_nonzero = -2f * vy_post / g;
        if (t2_nonzero <= 0f) return false;

        Vector3 deltaHoriz = vHoriz * t2_nonzero;
        pBounce2 = new Vector3(pBounce1.x + deltaHoriz.x, tableTopY, pBounce1.z + deltaHoriz.z);
        return true;
    }

    float TimeToReachY(float y0, float vy0, float targetY, float g)
    {
        float a = 0.5f * g;
        float b = vy0;
        float c = y0 - targetY;
        float disc = b * b - 4f * a * c;
        if (disc < 0f) return -1f;
        float sqrtD = Mathf.Sqrt(disc);
        float tA = (-b + sqrtD) / (2f * a);
        float tB = (-b - sqrtD) / (2f * a);
        float t = float.MaxValue;
        if (tA > 1e-5f) t = Mathf.Min(t, tA);
        if (tB > 1e-5f) t = Mathf.Min(t, tB);
        if (t == float.MaxValue) return -1f;
        return t;
    }

    float TimeToReachZ(float z0, float vz, float deltaZ)
    {
        if (Mathf.Abs(vz) < 1e-5f) return -1f;
        return deltaZ / vz;
    }
}

using System.Collections;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class AutoserveCll : MonoBehaviour
{
    [Header("References")]
    public Rigidbody ballRb;                  // optional: single scene ball (used if ballPrefab is null)
    public Transform ballSpawnParent;         // optional parent for instantiated balls
    public GameObject ballPrefab;             // recommended for auto-serving (will instantiate each serve)
    public Collider tableCollider;

    [Header("Table & Net geometry")]
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
    public float restitution = 0.8f;
    public float gravityY = -9.81f;
    [Tooltip("Base height above table to spawn the ball (meters). Random jitter added per serve).")]
    public float startHeightAboveTable = 0.30f;
    public float minTimeToFirstBounce = 0.08f;
    public float maxTimeToFirstBounce = 0.8f;

    [Header("Serve tuning")]
    public float firstBounceForwardOffset = 0.18f;
    public float firstBounceXOffsetRange = 0.05f;
    public int maxSolveAttempts = 200;

    [Header("Angle / inward bias (randomized)")]
    public float inwardBiasMin = 0.1f;
    public float inwardBiasMax = 0.5f;
    public float maxExtraLateralVel = 0.7f;
    public int lateralTweakAttempts = 8;

    [Header("Spin / realism")]
    public Vector2 randomSpinRange = new Vector2(10f, 50f); // degrees/sec range
    public Vector2 verticalPowerJitter = new Vector2(0.95f, 1.15f);
    public Vector2 netClearanceBoost = new Vector2(0.05f, 0.15f);
    public Vector2 startHeightJitter = new Vector2(-0.03f, 0.03f); // +/- jitter on spawn height

    [Header("Difficulty")]
    [Tooltip("Multiply serve speed. 1 = normal. 2 = twice as fast (same landing targets, less time).")]
    public float serveSpeed = 1f;
    [Tooltip("If > 0, automatic serving every 'autoDelay' seconds. Space still spawns manual serves.")]
    public float autoDelay = 0f;

    int serveIndex = 0;
    bool servingLeft = true;
    AudioSource audioSrc;
    public AudioClip serveSound;

    Coroutine autoServeRoutine;

    void Awake()
    {
        audioSrc = GetComponent<AudioSource>();
        gravityY = Physics.gravity.y;
        // clamp small positive
        serveSpeed = Mathf.Max(0.1f, serveSpeed);
    }

    void OnValidate()
    {
        // ensure sensible values while editing
        if (serveSpeed <= 0f) serveSpeed = 0.1f;
        if (minTimeToFirstBounce <= 0f) minTimeToFirstBounce = 0.02f;
        if (maxTimeToFirstBounce < minTimeToFirstBounce) maxTimeToFirstBounce = minTimeToFirstBounce + 0.1f;
    }

    void Start()
    {
        // start auto-serving if requested
        if (autoDelay > 0f)
        {
            autoServeRoutine = StartCoroutine(AutoServeLoop());
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            // manual extra serve (can coexist with auto-serving)
            StartCoroutine(DoServeRoutine(manualSpawn: true));
        }
    }

    IEnumerator AutoServeLoop()
    {
        while (true)
        {
            StartCoroutine(DoServeRoutine(manualSpawn: false));
            yield return new WaitForSeconds(autoDelay);
        }
    }

    /// <summary>
    /// DoServeRoutine now returns immediately after setting up a single serve.
    /// If ballPrefab is set we instantiate a new ball per serve and operate on that instance.
    /// If ballPrefab is null we fall back to using the scene ballRb (manual-only recommended).
    /// Parameter manualSpawn indicates whether this serve was triggered by space (true) or auto (false).
    /// </summary>
    IEnumerator DoServeRoutine(bool manualSpawn = false)
    {
        // create / choose the rigidbody for this serve
        Rigidbody rbForThisServe = null;

        if (ballPrefab != null)
        {
            var go = Instantiate(ballPrefab, Vector3.zero, Quaternion.identity, ballSpawnParent);
            rbForThisServe = go.GetComponent<Rigidbody>();
            if (rbForThisServe == null)
            {
                Debug.LogError("Ball prefab missing Rigidbody.");
                Destroy(go);
                yield break;
            }
            Destroy(go, 10f);
        }
        else
        {
            // no prefab: use provided scene ball
            rbForThisServe = ballRb;
            if (rbForThisServe == null)
            {
                Debug.LogError("No ball prefab and no scene ballRb assigned.");
                yield break;
            }
        }

        // small random spawn height jitter for realism
        float startHeight = startHeightAboveTable + Random.Range(startHeightJitter.x, startHeightJitter.y);

        Vector3 tableCenter = tableCollider ? tableCollider.bounds.center : Vector3.zero;

        int sideGroup = (serveIndex / 2) % 2;
        servingLeft = (sideGroup == 0);

        float spawnXLocal = servingLeft ? Random.Range(leftXMin, leftXMax) : Random.Range(rightXMin, rightXMax);
        float spawnZLocal = Random.Range(serveStartZMin, serveStartZMax);
        Vector3 spawnPos = new Vector3(spawnXLocal, tableTopY + startHeight, spawnZLocal) + new Vector3(tableCenter.x, 0f, tableCenter.z);

        rbForThisServe.transform.SetPositionAndRotation(spawnPos, Quaternion.identity);
        rbForThisServe.linearVelocity = Vector3.zero;
        rbForThisServe.angularVelocity = Vector3.zero;
        rbForThisServe.isKinematic = false;
        rbForThisServe.Sleep();

        float forwardSign = -1f;
        if (tableCollider != null)
        {
            forwardSign = Mathf.Sign(tableCenter.z - spawnPos.z);
            if (forwardSign == 0f) forwardSign = -1f;
        }

        // inward bias + candidate first-bounce (local -> world)
        float bias = Random.Range(inwardBiasMin, inwardBiasMax);
        float targetSideInnerX = servingLeft ? rightXMin : leftXMax;
        float p1XLocalBase = Mathf.Lerp(spawnXLocal, targetSideInnerX, bias);
        float p1XLocal = Mathf.Clamp(p1XLocalBase + Random.Range(-firstBounceXOffsetRange, firstBounceXOffsetRange),
                                      servingLeft ? leftXMin : rightXMin,
                                      servingLeft ? leftXMax : rightXMax);

        Vector3 p1Local = new Vector3(p1XLocal, tableTopY, spawnZLocal + forwardSign * Mathf.Abs(firstBounceForwardOffset));
        Vector3 p1WorldBase = p1Local + new Vector3(tableCenter.x, 0f, tableCenter.z);

        bool solved = false;
        Vector3 solvedVelocity = Vector3.zero;

        for (int attempt = 0; attempt < maxSolveAttempts; attempt++)
        {
            Vector3 candidateP1World = new Vector3(
                Mathf.Clamp(p1Local.x + Random.Range(-0.02f, 0.02f), servingLeft ? leftXMin : rightXMin, servingLeft ? leftXMax : rightXMax) + tableCenter.x,
                tableTopY,
                p1Local.z + Random.Range(-0.04f, 0.04f) + tableCenter.z
            );

            // pick a base time to first bounce, then scale it according to serveSpeed
            float baseT1 = Random.Range(minTimeToFirstBounce, maxTimeToFirstBounce);
            float scaledT1 = Mathf.Max(0.02f, baseT1 / serveSpeed); // shorter time if serveSpeed>1

            // compute initial velocity that arrives at candidateP1World at time scaledT1
            Vector3 vCandidate = SolveInitialVelocityForLandingAt(spawnPos, candidateP1World, scaledT1, gravityY);
            if (vCandidate == Vector3.zero) continue;

            // ensure lateral direction is inward; if not nudge candidateP1Local and continue
            if (servingLeft && vCandidate.x < 0f)
            {
                p1Local.x = Mathf.Lerp(p1Local.x, targetSideInnerX, 0.12f);
                continue;
            }
            if (!servingLeft && vCandidate.x > 0f)
            {
                p1Local.x = Mathf.Lerp(p1Local.x, targetSideInnerX, 0.12f);
                continue;
            }

            // try lateral tweaks with small vertical jitter (verticalPowerJitter applied after scaling)
            for (int lTry = 0; lTry < lateralTweakAttempts; lTry++)
            {
                float lateralMag = Random.Range(0f, maxExtraLateralVel);
                float lateralVel = servingLeft ? Mathf.Abs(lateralMag) : -Mathf.Abs(lateralMag);

                Vector3 vTweaked = new Vector3(vCandidate.x + lateralVel,
                                               vCandidate.y * Random.Range(verticalPowerJitter.x, verticalPowerJitter.y),
                                               vCandidate.z);

                // Predict second bounce using the tweaked velocity
                if (PredictSecondBounceLocation(spawnPos, vTweaked, candidateP1World, out Vector3 secondBounce))
                {
                    bool secondOnOpponentSide = (servingLeft
                        ? (secondBounce.x >= tableCenter.x + rightXMin && secondBounce.x <= tableCenter.x + rightXMax)
                        : (secondBounce.x >= tableCenter.x + leftXMin && secondBounce.x <= tableCenter.x + leftXMax));

                    // net clearance check with randomized boost
                    float netZ = tableCenter.z;
                    float timeToNet = TimeToReachZ(spawnPos.z, vTweaked.z, netZ - spawnPos.z);
                    bool clearsNet = true;
                    if (timeToNet > 0f)
                    {
                        float yAtNet = spawnPos.y + vTweaked.y * timeToNet + 0.5f * gravityY * timeToNet * timeToNet;
                        if (yAtNet < netTopY + Random.Range(netClearanceBoost.x, netClearanceBoost.y))
                            clearsNet = false;
                    }

                    if (secondOnOpponentSide && clearsNet)
                    {
                        // final small compensation for long diagonal paths
                        float pathStretch = Mathf.Abs(vTweaked.x) / Mathf.Max(0.01f, Mathf.Abs(vTweaked.z));
                        float verticalBoost = Mathf.Lerp(0.02f, 0.08f, Mathf.Clamp01(pathStretch * 1.5f));
                        vTweaked.y += verticalBoost;

                        // ensure net clearance after boost
                        if (timeToNet > 0f)
                        {
                            float yAtNet2 = spawnPos.y + vTweaked.y * timeToNet + 0.5f * gravityY * timeToNet * timeToNet;
                            if (yAtNet2 < netTopY + 0.04f)
                                vTweaked.y += (netTopY + 0.05f - yAtNet2) * 1.1f;
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
            Debug.LogWarning("Autoserve: fallback serve used.");
            float fallbackLateral = servingLeft ? 0.25f : -0.25f;
            // apply serveSpeed to fallback: faster means larger horizontal & forward components, keep vertical similar
            solvedVelocity = new Vector3(fallbackLateral * serveSpeed, 1.2f * Mathf.Clamp(serveSpeed, 0.7f, 1.6f), forwardSign * 2.6f * serveSpeed);
        }

        // apply velocities to this serve's rigidbody
        rbForThisServe.linearVelocity = solvedVelocity;
        rbForThisServe.angularVelocity = new Vector3(
            Random.Range(-randomSpinRange.y, randomSpinRange.y),
            Random.Range(-randomSpinRange.x, randomSpinRange.x),
            Random.Range(-randomSpinRange.y, randomSpinRange.y)
        ) * Mathf.Deg2Rad;

        if (serveSound && audioSrc) audioSrc.PlayOneShot(serveSound);

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
        if (v.magnitude > 120f) return Vector3.zero; // avoid totally unrealistic speeds
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
        return t == float.MaxValue ? -1f : t;
    }

    float TimeToReachZ(float z0, float vz, float deltaZ)
    {
        if (Mathf.Abs(vz) < 1e-5f) return -1f;
        return deltaZ / vz;
    }
}

using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class WindAffectedObject : MonoBehaviour
{
    [Tooltip("Multiplier of how the wind will affect the object.")]
    public float forceMultiplier = 1.0f;

    private Rigidbody rb;
    private WindZone windZone;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        windZone = FindFirstObjectByType<WindZone>();
    }

    private void FixedUpdate()
    {
        if (windZone == null) return;

        Vector3 windforce = windZone.transform.forward * windZone.windMain * forceMultiplier;
        rb.AddForce(windforce, ForceMode.Force);
    }
}
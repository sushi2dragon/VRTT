using UnityEngine;

public class BallCleanup : MonoBehaviour
{
    void Start()
    {
       
       
    }

    void OnCollisionEnter(Collision collision)
    {
        
        if (collision.collider.CompareTag("Floor"))
        {
            Destroy(gameObject);
        }
    }

    System.Collections.IEnumerator DestroyAfterTime(float delay)
    {
        yield return new WaitForSeconds(delay);
        Destroy(gameObject);
    }
}

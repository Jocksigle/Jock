using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Bullect : MonoBehaviour
{
    public GameObject target;

    private void Update()
    {
        if(target != null)
        {
            Vector2 dire = target.transform.position - transform.position;
            dire.Normalize();
            transform.position += new Vector3(dire.x, dire.y, 0) * 5 * Time.deltaTime;
            Destroy(gameObject, 1.5f);
        }
    }
}
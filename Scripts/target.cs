using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class target : MonoBehaviour
{
    public Transform nextTarget;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }  
    private void OnCollisionEnter2D(Collision2D collision)
    {

        if (nextTarget != null && collision.transform.CompareTag("Enemy"))
        {
            collision.transform.GetComponent<Monster>().target = nextTarget;
        }
    }
}

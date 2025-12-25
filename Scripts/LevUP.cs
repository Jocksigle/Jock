using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class LevUP : MonoBehaviour,IPointerDownHandler
{
    public Transform TowerPos;
    public GameObject newTower;


    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    public void OnPointerDown(PointerEventData eventData)
    {      
        Vector3 pos=TowerPos.position;
        Destroy(TowerPos.gameObject);
        Instantiate(newTower,pos, Quaternion.identity);
        Destroy(this.gameObject);
    }
}

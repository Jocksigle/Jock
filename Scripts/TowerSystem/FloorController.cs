using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;

public class FloorController : MonoBehaviour,IPointerDownHandler
{
    [SerializeField]
    private GameObject towerPrefab;


    private void Start()
    { 

    }

    private void Update()
    {
       
    }

    private void ChangeChoice()
    {
        if (towerPrefab!=null && towerPrefab.activeSelf)
        {
            towerPrefab.SetActive(false);
        }
        else if(towerPrefab != null)
        {
            towerPrefab.SetActive(true);
            GetComponent<AudioSource>().PlayOneShot(GameFacade.Instance.LoadAudio("Feeds/GiftBox01"));
        }
    }


    void IPointerDownHandler.OnPointerDown(PointerEventData eventData)
    {
         ChangeChoice();
    }
  
}
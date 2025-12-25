using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class TowerController : MonoBehaviour, IPointerDownHandler
{
    [SerializeField]
    private GameObject LevUp;

    public GameObject tower;
    public GameObject floor;
    public GameObject SpawnPoint;

    private void SpawnTower()
    {
        Destroy(gameObject);
        LevUp.SetActive(true);
        tower.SetActive(true);
        SpawnPoint.GetComponent<Tower>().isStart = true;
        floor.GetComponent<CanvasGroup>().alpha = 0;
    }

    void IPointerDownHandler.OnPointerDown(PointerEventData eventData)
    {
        SpawnTower();
    }
}
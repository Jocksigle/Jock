using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 塔的共同特性
/// </summary>
public class Tower : MonoBehaviour
{
    public Queue<GameObject> enemys = new Queue<GameObject>();
    public GameObject bulletPrefab;
    public bool isStart = false;

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!isStart) return;
        enemys.Enqueue(collision.gameObject);
        StartCoroutine(SpawnBullet());
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (enemys.Count > 0)
        {
            enemys.Dequeue();
            if (enemys.Count <= 0 )
            {
                StopCoroutine(SpawnBullet());
            }
        }
    }

    private IEnumerator SpawnBullet()
    {
        GameObject bullet = Instantiate(bulletPrefab, this.transform.position, Quaternion.Euler(new Vector3(90, 90, 0)));
        bullet.GetComponent<Bullect>().target = enemys.Peek();
        yield return new WaitForSeconds(3.0f);
        if (enemys.Count > 0)
        {
            StartCoroutine(SpawnBullet());
        }
    }
}
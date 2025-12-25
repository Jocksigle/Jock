using System.Collections;
using UnityEngine;

public class SpawnController : MonoBehaviour
{
    [SerializeField]
    private GameObject spawnPoint;

    public GameObject[] monsters1;
    public GameObject SpawnPointer;
    //private bool isSpawn = true;

    [SerializeField]
    private float cd = 2f;

    private void Start()
    {
        StartCoroutine(Spaen2(0, 5));
    }

    private void Update()
    {
    }

    //游戏被判定为结束，停止调用动物生成的方法
    public void StopSpawn()
    {
        //停止当前脚本的Invoke
        CancelInvoke();
    }

    public IEnumerator Spaen2(int monIndex, int num)
    {
        for (int i = 0; i < num; i++)
        {
            yield return new WaitForSeconds(cd);
            GameObject go = Instantiate(monsters1[monIndex]);
            go.transform.SetParent(SpawnPointer.transform, true);
            go.transform.position = spawnPoint.transform.position;
        }

        yield return new WaitForSeconds(2f);

        StartCoroutine(Spaen2(++monIndex, num));

        if (monIndex == 3)
        {
            StopAllCoroutines();
        }
    }

    //public void newSpawn(int monIndex, int num)
    //{
    //    for (int i = 0; i < num; i++)
    //    {
    //        float timer = 0;
    //        while (timer <= cd)
    //        {
    //            timer += Time.deltaTime;
    //        }
    //        GameObject go = Instantiate(monsters1[monIndex]);
    //        go.transform.position = spawnPoint.transform.position;
    //    }
    //    isSpawn = false;
    //}
}
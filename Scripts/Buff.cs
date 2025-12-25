using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class Buff : MonoBehaviour
{
    public GameObject EnemyParent;

    public Image img_fire;
    public Image img_ice;

    public void ice()
    {
        StartCoroutine(Ice());

        StartCoroutine(Img_ice());
    } 

    public void slow()
    {
        StartCoroutine(Slow());
    }

    public IEnumerator Ice()
    {
        for (int i = 0; i < EnemyParent.transform.childCount; i++)
        {
            EnemyParent.transform.GetChild(i).GetComponent<Monster>().speed = 0;
        }
        yield return new WaitForSeconds(1.0f);
        for (int i = 0; i < EnemyParent.transform.childCount; i++)
        {
            EnemyParent.transform.GetChild(i).GetComponent<Monster>().speed = 2;
        }
    }

    public IEnumerator Slow()
    {
        for (int i = 0; i < EnemyParent.transform.childCount; i++)
        {
            Transform enemy = EnemyParent.transform.GetChild(i);
            enemy.GetComponent<Monster>().speed = 0.5f;
        }
        yield return new WaitForSeconds(2.0f);
        for (int i = 0; i < EnemyParent.transform.childCount; i++)
        {
            Transform enemy = EnemyParent.transform.GetChild(i);
            enemy.GetComponent<Monster>().speed = 2f;
        }
    }

    public void fire()
    {
        for (int i = 0; i < EnemyParent.transform.childCount; i++)
        {
            Destroy(EnemyParent.transform.GetChild(i).gameObject, 0.8f);
            StartCoroutine(Img_fire());
        }      

    }

   public IEnumerator Img_ice()
    {
        img_ice.gameObject.SetActive(true);
        yield return new WaitForSeconds(1.0f);
        img_ice.gameObject.SetActive(false);
    }

    public IEnumerator Img_fire()
    {
        img_fire.gameObject.SetActive(true);
        yield return new WaitForSeconds(0.7f);
        img_fire.gameObject.SetActive(false);
    }
}
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class Items : MonoBehaviour
{
    public Slider slider;

    private int hp = 150;
    private int hpTotal;

    // Start is called before the first frame update
    void Start()
    {
        hpTotal = hp;
    }

   public void GetDamage()
    {
        hp-=Random.Range(15, 20);
        slider.value = (float)hp / hpTotal;
        if (hp <= 0)
        {
            Destroy(this.gameObject);
            GameFacade.Instance.Coin();
        }
    }
}

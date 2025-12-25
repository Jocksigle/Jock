using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class Carrot : MonoBehaviour
{
    public Text hp;//萝卜血量

    private int hpValue = 10;
    public GameController controller;

    public void CarrotHp()
    {
        hpValue--;
        hp.text = hpValue.ToString();
    }

    //private void OnCollisionEnter2D(Collision2D collision)
    //{
    //    if (collision.collider.CompareTag("Enemy"))
    //    {
    //        CarrotHp();
    //        Destroy(collision.gameObject);
    //        if (hpValue <= 0)
    //        {
    //            Destroy(gameObject);
    //            EventCenter.Broadcast(EventNum.GAMEOVER);
    //        }
    //    }
    //}
    private void OnTriggerEnter2D(Collider2D collision)
    {
        controller.fail();
    }
}
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.AI;

public class Monster : MonoBehaviour
{
   // private NavMeshAgent agent;

    public Transform target;

    public Slider hpSlider;
    public int MonIndex;

    [SerializeField]
    public float speed = 10f;

    [SerializeField]
    private int hp = 50;

    private int hpTotal;
   

    private void Start()
    {
        hpTotal = hp;
        target = GameObject.Find("target").transform;

    }

    private void Update()
    {
        //判断游戏是否结束
        //if (GameFacade.Instance.GetGameOverValue()) return; 
        Vector2 dire=target.position-transform.position;
        dire.Normalize();
        transform.position+=new Vector3(dire.x,dire.y,0) *speed*Time.deltaTime;
    }

    public void GetDamage()
    {
        hp -= Random.Range(10, 20);
        hpSlider.value = (float)hp / hpTotal;
        if (hp <= 0)
        {
            GameObject.Destroy(this.gameObject);
            GameFacade.Instance.Coin();
        }
    }

    //碰撞检测发生条件：两个物体必须有碰撞体，并且至少有一个物体具有刚体组件
    //UI最主要内容：锚点、
    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.collider.CompareTag("Bullect"))
        {
            //销毁子弹
            //音效
            //Debug.Log(GameFacade.Instance.LoadAudio("Feeds/GiftBox03"));
            GetComponent<AudioSource>().PlayOneShot(GameFacade.Instance.LoadAudio("Feeds/GiftBox03"));
            GetDamage();
            Destroy(collision.gameObject);
        }
        
    }
}
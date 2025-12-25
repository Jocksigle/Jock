using UnityEngine;
using UnityEngine.UI;

public class GameController : MonoBehaviour
{
    public Text coin;//金币

    private bool mGameOver = false;//判断游戏是否结束
    public GameObject Fail;
    public GameObject Win;
    private float timer=0f;

    private int coinSum ;

    public bool MGameOver { get => mGameOver; private set => mGameOver = value; }

    //获取组件的两种方式：
    //1、特殊组件Transform直接获取
    //2、除Transform之外所有组件都使用GetComponent<>()  泛型里面是组件名称
    private void Start()
    {
    }

    public void Coin()
    {
        coinSum += 100;
        coin.text = coinSum.ToString();
    }

    private void Update()
    {
        timer += Time.deltaTime;
        if (timer > 120)
        {
            win();
        }
    }
    public void win()
    {
        Time.timeScale = 0;
        Win.SetActive(true);
    }
    public void fail()
    {
        Time.timeScale = 0;
        Fail.SetActive(true);
    }
}
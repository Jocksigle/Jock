using UnityEngine;
using UnityEngine.UI;

public class StartState : ISceneState
{
    private Transform UIRoot;
    private Transform Str;
    private Transform CheckPoint;
    private Transform Btn;

    public StartState(SceneStateControl control, string scenename) : base(scenename, control)
    {
    }

    public override void StateStart()
    {
        base.StateStart();

        UIRoot = GameObject.Find("Canvas").transform;
        Str = UIRoot.Find("Str");
        CheckPoint = UIRoot.Find("CheckPoint");
        Btn = UIRoot.Find("Btn");        

        Str.GetChild(1).GetComponent<Button>().onClick.AddListener(() =>
        {
            Str.gameObject.SetActive(false);
            CheckPoint.gameObject.SetActive(false);
            Btn.gameObject.SetActive(true);
        });

        Btn.GetChild(0).GetComponent<Button>().onClick.AddListener(() =>
        {
            Str.gameObject.SetActive(false);
            CheckPoint.gameObject.SetActive(true);
            Btn.gameObject.SetActive(false);
        });

        //退出
        Btn.GetChild(1).GetComponent<Button>().onClick.AddListener(() =>
        {
            UnityEditor.EditorApplication.isPlaying = false;
            Application.Quit();
        });

        //开始游戏
        CheckPoint.GetChild(0).GetComponent<Button>().onClick.AddListener(() =>
        {
            control.SetState(new LoadState(control, "1"));
        });

        CheckPoint.GetChild(1).GetComponent<Button>().onClick.AddListener(() =>
        {
            control.SetState(new LoadState(control, "2"));
        });

        //返回
        CheckPoint.GetChild(2).GetComponent<Button>().onClick.AddListener(() =>
        {
            Str.gameObject.SetActive(false);
            CheckPoint.gameObject.SetActive(false);
            Btn.gameObject.SetActive(true);
        });
    }

    public override void StateEnd()
    {
        base.StateEnd();
    }
}
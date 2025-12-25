using UnityEngine;
using UnityEngine.UI;

public class GameState : ISceneState
{
    private string scene;

    public GameState(SceneStateControl control, string scenename) : base(scenename, control)
    {
        scene = scenename;
    }

    private Transform UIRoot;
    private Transform P_Menu;


    private bool isPausing = false;

    public bool IsPausing
    {
        get { return isPausing; }
        set
        {
            isPausing = value;
        }
    }

    public override void StateStart()
    {
        base.StateStart();

        UIRoot = GameObject.Find("Canvas").transform;
        P_Menu = UIRoot.Find("P_Menu");

        #region menu

        UIRoot.GetChild(0).GetChild(4).GetComponent<Button>().onClick.AddListener(() =>
        {
            //菜单
            P_Menu.gameObject.SetActive(true);
        });

        UIRoot.GetChild(0).GetChild(3).GetComponent<Button>().onClick.AddListener(() =>
        {
            //暂停
            isPausing = false;
            //p.gameObject.SetActive(true);
            //s.gameObject.SetActive(false);
        });

        UIRoot.GetChild(0).GetChild(2).GetComponent<Button>().onClick.AddListener(() =>
        {
            //开始
            isPausing = true;
            //p.gameObject.SetActive(false);
            //s.gameObject.SetActive(true);
        });

        UIRoot.GetChild(1).GetChild(1).GetComponent<Button>().onClick.AddListener(() =>
        {
            //暂停结束 继续游戏
            P_Menu.gameObject.SetActive(false);
        });

        UIRoot.GetChild(1).GetChild(2).GetComponent<Button>().onClick.AddListener(() =>
        {
            //重新开始
            if (scene == "1")
                control.SetState(new GameState(control, "1"));
            if (scene == "2")
                control.SetState(new GameState(control, "2"));
        });

        UIRoot.GetChild(1).GetChild(3).GetComponent<Button>().onClick.AddListener(() =>
        {
            //返回主页
            control.SetState(new StartState(control, "Start"));
        });

        #endregion

        #region endpanel
        //结算页面
        UIRoot.GetChild(2).GetChild(0).GetChild(1).GetComponent<Button>().onClick.AddListener(() =>
        {
            //返回主页
            control.SetState(new StartState(control, "Start"));
        });

        UIRoot.GetChild(2).GetChild(0).GetChild(2).GetComponent<Button>().onClick.AddListener(() =>
        {
            //重新开始
            if (scene == "1")
                control.SetState(new GameState(control, "1"));
            if (scene == "2")
                control.SetState(new GameState(control, "2"));
        });

        UIRoot.GetChild(2).GetChild(0).GetChild(3).GetComponent<Button>().onClick.AddListener(() =>
        {
            //下一关
            if (scene == "1")
                control.SetState(new GameState(control, "2"));
            if (scene == "2")
                control.SetState(new GameState(control, "1"));
        });

        //结算页面
        UIRoot.GetChild(2).GetChild(1).GetChild(1).GetComponent<Button>().onClick.AddListener(() =>
        {
            //返回主页
            control.SetState(new StartState(control, "Start"));
        });

        UIRoot.GetChild(2).GetChild(1).GetChild(2).GetComponent<Button>().onClick.AddListener(() =>
        {
            //重新开始
            if (scene == "1")
                control.SetState(new GameState(control, "1"));
            if (scene == "2")
                control.SetState(new GameState(control, "2"));
        });

        UIRoot.GetChild(2).GetChild(1).GetChild(3).GetComponent<Button>().onClick.AddListener(() =>
        {
            //下一关
            if (scene == "1")
                control.SetState(new GameState(control, "2"));
            if (scene == "2")
                control.SetState(new GameState(control, "1"));
        });
        #endregion
    }

    public override void StateEnd()
    {
        base.StateEnd();

    }
}
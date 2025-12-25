using UnityEngine;
using UnityEngine.UI;

public class LoadState : ISceneState
{
    private string scene;

    public LoadState(SceneStateControl control, string scenename) : base("Loading", control)
    {
        scene = scenename;
    }

    private Image loadBar;

    private float waitTime = 0.0f;
    private float allTime = 5.0f;

    public override void StateStart()
    {
        base.StateStart();
        loadBar = GameObject.Find("loadBar").GetComponent<Image>();
    }

    public override void StateUpdate()
    {
        base.StateUpdate();
        waitTime += Time.deltaTime;
        loadBar.fillAmount = waitTime / allTime;

        if (waitTime > allTime)
        {
            if (scene == "1")
                control.SetState(new GameState(control, "1"));
            if (scene == "2")
                control.SetState(new GameState(control, "2"));
        }
    }

    public override void StateEnd()
    {
        base.StateEnd();
    }
}
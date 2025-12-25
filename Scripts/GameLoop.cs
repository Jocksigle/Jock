using UnityEngine;

public class GameLoop : MonoBehaviour
{
    private SceneStateControl stateControl;
    private StartState startState;

    private static GameLoop instance;

    public static GameLoop Instance
    {
        get { return instance; }
    }

    private void Awake()
    {
        instance = this;

        //跳转场景不销毁物体
        if (GameObject.Find("GameLoop").gameObject != this.gameObject)
            Destroy(this.gameObject);
        DontDestroyOnLoad(this.gameObject);
    }

    // Start is called before the first frame update
    private void Start()
    {
        stateControl = new SceneStateControl();

        stateControl.SetState(new StartState(stateControl,""), false);
    }

    // Update is called once per frame
    private void Update()
    {
        if (stateControl != null)
            stateControl.StateUpdate();
    }
}
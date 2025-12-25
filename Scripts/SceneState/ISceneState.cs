public class ISceneState
{
    private string mSceneName;

    protected SceneStateControl control;

    public string MSceneName { get => mSceneName; set => mSceneName = value; }

    public ISceneState(string sceneName, SceneStateControl control)
    {
        mSceneName = sceneName;
        this.control = control;
    }

    public virtual void StateStart()
    { }

    public virtual void StateEnd()
    { }

    public virtual void StateUpdate()
    { }
}
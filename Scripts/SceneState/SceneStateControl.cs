using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneStateControl
{
    private ISceneState sceneState;

    private AsyncOperation mAo;

    private bool mIsRunStart = false;

    public void SetState(ISceneState sceneState, bool isLoadScene = true)
    {
        if (this.sceneState != null)
            this.sceneState.StateEnd();

        this.sceneState = sceneState;

        if (isLoadScene)
        {
            mAo = SceneManager.LoadSceneAsync(sceneState.MSceneName);
            mIsRunStart = false;
        }
        else
        {
            this.sceneState.StateStart();
            mIsRunStart = true;
        }
    }

    public void StateUpdate()
    {
        if (mAo != null && !mAo.isDone) return;

        if (mAo != null && mAo.isDone && mIsRunStart == false)
        {
            sceneState.StateStart();
            mIsRunStart = true;
        }
        if (sceneState != null)
        {
            sceneState.StateUpdate();
        }
    }
}
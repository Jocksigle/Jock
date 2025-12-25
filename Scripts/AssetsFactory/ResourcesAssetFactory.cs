using UnityEngine;

public class ResourcesAssetFactory : IAssetFactory
{
    public string AudioPath = "Sound/";
    public string TowerPath = "Tower/";
    public string ItemPath = "Items/";
    public string EnemyPath = "Enemy/";

    public AudioClip LoadAudio(string name)
    {
        return Resources.Load<AudioClip>(AudioPath + name);
    }

    public GameObject LoadEnemy(string name)
    {
        return InstantiateGameObject(EnemyPath + name);
    }

    public Sprite LoadItem(string name)
    {
        return Resources.Load<Sprite>(ItemPath + name);
    }

    public GameObject LoadTower(string name)
    {
        return InstantiateGameObject(TowerPath + name);
    }

    public GameObject InstantiateGameObject(string path)
    {
        Object obj = Resources.Load(path);
        if (obj == null)
        {
            Debug.LogError("无法加载资源，路径：" + path);
            return null;
        }
        return GameObject.Instantiate(obj) as GameObject;
    }

    public Object LoadAsset(string path)
    {
        Object obj = Resources.Load(path);
        if (obj == null)
        {
            Debug.LogError("无法加载资源，路径：" + path);
            return null;
        }
        return obj;
    }
}
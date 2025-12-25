using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ResourcesAssetProxy : IAssetFactory
{
    private ResourcesAssetFactory assetFactory = new ResourcesAssetFactory();

    private Dictionary<string, AudioClip> mSound = new Dictionary<string, AudioClip>();
    private Dictionary<string, GameObject> mTower = new Dictionary<string, GameObject>();
    private Dictionary<string, GameObject> mEnemy = new Dictionary<string, GameObject>();
    private Dictionary<string, Sprite> mItem = new Dictionary<string, Sprite>();

    public AudioClip LoadAudio(string name)
    {
        if (mSound.ContainsKey(name))
        {
            return mSound[name];
        }
        else
        {
            AudioClip asset = assetFactory.LoadAudio(name) as AudioClip;
            mSound.Add(name, asset);
            return asset;
        }
    }

    public GameObject LoadEnemy(string name)
    {
        if (mEnemy.ContainsKey(name))
        {
            return GameObject.Instantiate(mEnemy[name]);
        }
        else
        {
            GameObject asset = assetFactory.LoadAsset(assetFactory.EnemyPath + name) as GameObject;
            mEnemy.Add(name, asset);
            return GameObject.Instantiate(asset);
        }
    }

    public Sprite LoadItem(string name)
    {
        if (mItem.ContainsKey(name))
        {
            return mItem[name];
        }
        else
        {
            Sprite asset = assetFactory.LoadItem( name) as Sprite;
            mItem.Add(name, asset);
            return asset;
        }
    }

    public GameObject LoadTower(string name)
    {
        if (mTower.ContainsKey(name))
        {
            return GameObject.Instantiate(mTower[name]);
        }
        else
        {
            GameObject asset = assetFactory.LoadAsset(assetFactory.TowerPath + name) as GameObject;
            mTower.Add(name, asset);
            return GameObject.Instantiate(asset);
        }
    }
}
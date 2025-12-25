using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IAssetFactory
{
    AudioClip LoadAudio(string name);
    GameObject LoadTower(string name);
    GameObject LoadEnemy(string name);
    Sprite LoadItem(string name);
}
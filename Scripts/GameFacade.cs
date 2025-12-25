using UnityEngine;

public class GameFacade : MonoBehaviour
{
    public static GameFacade instance;

    private IAssetFactory assetFactory;

    private GameController gameController;
    private SpawnController spawnController;
    private Carrot carrot;
    private Items items;
    private Monster monster;

    public static GameFacade Instance
    {
        get { return instance; }
    }

    private void Awake()
    {
        instance = this;
        assetFactory = new ResourcesAssetProxy();
        gameController = GameObject.Find("Canvas").GetComponent<GameController>();
        spawnController = GetComponent<SpawnController>();
        carrot = GetComponent<Carrot>();
    }

    public GameObject LoadTower(string name)
    {
        return assetFactory.LoadTower(name);
    }

    public Sprite LoadItem(string name)
    {
        return assetFactory.LoadItem(name);
    }

    public GameObject LoadEnemy(string name)
    {
        return assetFactory.LoadEnemy(name);
    }

    public AudioClip LoadAudio(string name)
    {
        return assetFactory.LoadAudio(name);
    }

    public void CarrotHp()
    {
        carrot.CarrotHp();
    }

    public void Coin()
    {
        gameController.Coin();
    }

    public void StopSpawn()
    {
        spawnController.StopSpawn();
    }

    public void ItemsGetDamage()
    {
        items.GetDamage();
    }

    public void MonsterGetDamage()
    {
        monster.GetDamage();
    }
}
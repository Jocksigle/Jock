using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System;
public class Comm : MonoBehaviour
{

    public Text text1;
    public bool isPlayerVideo;
    public static Comm instance;
    public SerialPortUtility.SerialPortUtilityPro serialPort;

    public bool[] anjian = new bool[8];
    bool isfalse = false;
    public bool[] deng = new bool[32];
    public bool[] cdeng = new bool[32];
    float time = 0;
    float dengtime = 1.5f;
    int currint = 0;

    private void Awake()
    {
        instance = this;
    }

    private void OnDestroy()
    {
        send(currint, false);
        serialPort.Close();
    }


    // Start is called before the first frame update
    void Start()
    {

        for (int i = 0; i < deng.Length; i++)
        {
            deng[i] = false;
            cdeng[i] = true;
        }
    }




    // Update is called once per frame
    void Update()
    {

    }
    bool sendmsg = false;
    private void FixedUpdate()
    {
        if (!serialPort.IsOpened()) return;
        if (dengtime <= 0)
        {
            dengtime = 0;
            isfalse = false;
            for (int i = 0; i < 1; i++)
            {
                if (deng[i] != cdeng[i])
                {
                    cdeng[i] = deng[i];
                    send(i, deng[i]);
                    dengtime = 0.03f;
                }
            }
        }
        else
        {
            dengtime -= Time.fixedDeltaTime;

        }
    }
    

    void Write(byte[] _byte)
    {
        serialPort.Write(_byte);
    }

    //发送数据
    void send()
    {
        writeOpen();
    }
    void send(int tmpint, bool tmpbool)
    {
        if (tmpbool) sendon(tmpint);
        else sendoff(tmpint);
    }

    void sendoff(int tmpint)
    {
        //print(Convert.ToString(tmpint + 1, 16));
        string buf = "1A 2B 3C 4D 0C 00 97 03 01 02 03 D8";
        print(buf);
        Write(strToHexByte(buf));
    }
    void sendon(int tmpint)
    {
        string buf = "1A 2B 3C 4D 0C 00 93 03 01 02 03 DC";
        print(buf);
        Write(strToHexByte(buf));
    }

    void writeOpen()
    {
        Invoke("delaywrite", 0.15f);
    }

    public void ReadComplateProcessing(object data)
    {
        string str = "";
        var bin2 = data as byte[];
        //text1.text = byteToHexStr(bin);
        //Debug.Log(byteArray);
        //Debug.Log(sw.Elapsed.Milliseconds);
        text1.text = byteToHexStr(bin2);
        str = text1.text;
        if (str == "1A 2B 3C 4D 0D 00 97 00 03 01 02 03 D9".Replace(" ", ""))
        {
            print("同时关多个灯成功");
        }
        else if (str == "1A 2B 3C 4D 0D 00 93 00 03 01 02 03 D9".Replace(" ",""))
        {
            print("同时开多个灯成功");
        }
        if (str.Contains("1A2B3C4D0E009C040100000000D7"))
        {
            anjian[0] = false;
        }
        else if (str.Contains("1A2B3C4D0E009C040000000000D6"))
        {
            anjian[0] = true;
        }
    }
    public int merge(byte high, byte low)
    {
        return (((0x000000ff & high) << 8) & 0x0000ff00) | (0x000000ff & low);
    }

    public static int byte2Int(byte[] b)
    {
        int intValue = 0;
        for (int i = 0; i < b.Length; i++)
        {
            intValue += (b[i] & 0xFF) << (8 * (3 - i));
        }
        return intValue;
    }

    public static string byteToHexStr(byte[] bytes)
    {
        string returnStr = "";
        if (bytes != null)
        {
            for (int i = 0; i < bytes.Length; i++)
            {
                returnStr += bytes[i].ToString("X2");
            }
        }
        return returnStr;
    }

    public static string byteToHexStr(byte bytes)
    {
        return bytes.ToString("X2");
    }

    private static byte[] strToHexByte(string hexString)
    {
        hexString = hexString.Replace(" ", "");
        if ((hexString.Length % 2) != 0)
            hexString += " ";
        //print(hexString);
        byte[] returnBytes = new byte[hexString.Length / 2];
        for (int i = 0; i < returnBytes.Length; i++)
            returnBytes[i] = Convert.ToByte(hexString.Substring(i * 2, 2), 16);
        return returnBytes;
    }

    /// <summary>
    /// 异或值
    /// </summary>
    /// <param name="data"></param>
    /// <returns></returns>
    public static byte GetXOR(byte[] data)
    {
        byte xor = 0;
        for (int i = 0; i < data.Length; i++)
        {
            xor ^= data[i];
        }
        return xor;
    }
}

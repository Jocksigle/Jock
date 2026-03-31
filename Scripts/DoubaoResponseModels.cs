using System;
using System.Collections.Generic;

[Serializable]
public class DoubaoServerResponse
{
    public DoubaoResult result;
    public DoubaoAudioInfo audio_info;
}

[Serializable]
public class DoubaoResult
{
    public string text;
    public List<DoubaoUtterance> utterances;
}

[Serializable]
public class DoubaoUtterance
{
    public bool definite;
    public int start_time;
    public int end_time;
    public string text;
}
[Serializable]
public class DoubaoAudioInfo
{
    public int duration;
}
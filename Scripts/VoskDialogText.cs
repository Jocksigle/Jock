using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Text.RegularExpressions;
using TMPro;
using System.Collections.Generic;
using System.Collections; // 新增

public class VoskDialogText : MonoBehaviour
{
    [Header("UI References")]
    public DoubaoSpeechToText DoubaoSpeechToText;
    public TMP_Text DialogText;
    public Button startStopButton; // 按住录音/松开停止的单按钮
    public TMP_Text statusText;

    [Header("API Settings")]
    public string roleName = "东海龙王"; // 可以设置为不同的龙王
    public bool sendAudioWithText = true; // 是否同时发送音频
    public float maxRecordingTime = 10f; // 最大录音时间

    [Header("Display Settings")]
    public int maxDisplayLines = 20;
    public Color userTextColor = Color.blue;
    public Color aiTextColor = Color.green;
    public Color systemTextColor = Color.yellow;
    public Color errorTextColor = Color.red;
    public Color recordingColor = Color.red;

    [Header("Waiting Audio")]
    public AudioSource waitingAudioSource;
    public AudioClip eastWaitingClip;
    public AudioClip westWaitingClip;
    public AudioClip southWaitingClip;
    public AudioClip northWaitingClip;
    public AudioClip defaultWaitingClip;

    private bool isProcessingApiResponse = false;
    private bool isRecordingAudio = false;
    private bool interactionsReady = false; // 初始化完成前禁用交互
    private string lastThinkingMessage = "";
    private string lastRecognizedText = ""; // 记录实时识别的最新文本，用于上传
    private Coroutine waitingAudioRoutine; // 新增：等待音效协程句柄

    void Awake()
    {
        if (DoubaoSpeechToText != null)
        {
            //DoubaoSpeechToText.OnTranscriptionResult += OnTranscriptionResult;
        }
        else
        {
            Debug.LogError("DoubaoSpeechToText 未分配！");
        }

        // 初始化对话显示
        if (DialogText != null)
        {
            DialogText.text = $"=== 语音对话系统 (角色: {roleName}) ===\n\n";
            AddSystemMessage($"系统已启动，当前角色: {roleName}，请开始说话...");
        }

        //// 订阅API响应事件
        //if (CozeApiManager.Instance != null)
        //{
        //    CozeApiManager.Instance.OnApiResponseReceived += OnApiResponseTextReceived;
        //}

        // 初始化 Start/Stop 单按钮，初始化完成前先禁用
        if (startStopButton != null)
        {
            startStopButton.interactable = false;
            RegisterStartStopButtonEvents(startStopButton);
        }

        // 标记初始化完成，允许交互
        interactionsReady = true;
        if (startStopButton != null)
        {
            startStopButton.interactable = true;
        }
    }

    void OnDestroy()
    {
        StopWaitingAudio();
        if (DoubaoSpeechToText != null)
        {
            //DoubaoSpeechToText.OnTranscriptionResult -= OnTranscriptionResult;
        }

        //if (CozeApiManager.Instance != null)
        //{
        //    CozeApiManager.Instance.OnApiResponseReceived -= OnApiResponseTextReceived;
        //}
    }

    void Update()
    {
        // 更新状态文本
        if (statusText != null)
        {
            if (isRecordingAudio)
            {
                statusText.text = "正在录音...";
                statusText.color = recordingColor;
            }
            //else if (CozeApiManager.Instance != null && CozeApiManager.Instance.IsPlayingAudio())
            //{
            //    statusText.text = "正在播放音频...";
            //    statusText.color = Color.green;
            //}
            else if (isProcessingApiResponse)
            {
                statusText.text = "正在处理...";
                statusText.color = Color.yellow;
            }
            else
            {
                statusText.text = "准备就绪";
                statusText.color = Color.white;
            }
        }

        // 单按钮的可交互控制：初始化完成且不在处理 API 时可按住录音
        if (startStopButton != null)
        {
            startStopButton.interactable = interactionsReady && !isProcessingApiResponse;
        }
    }

    private void OnTranscriptionResult(string jsonResult)
    {
        if (string.IsNullOrEmpty(jsonResult))
        {
            Debug.LogWarning("收到空的识别结果");
            return;
        }

        try
        {
            //var result = new RecognitionResult(jsonResult);

            //if (result == null || result.Phrases == null || result.Phrases.Length == 0)
            //{
            //    Debug.LogWarning("解析识别结果失败或没有有效短语");
            //    return;
            //}

            //string recognizedText = result.Phrases[0].Text;

            //if (string.IsNullOrEmpty(recognizedText))
            //{
            //    Debug.Log("识别到空文本，跳过处理");
            //    return;
            //}

            //Debug.Log($"识别到的文本: {recognizedText}");
            //lastRecognizedText = recognizedText;

            //// 显示用户说的话
            //AddUserMessage(recognizedText);

            // 检查是否已经在处理API响应
            if (isProcessingApiResponse)
            {
                AddSystemMessage("正在处理上一个请求，请稍候...");

                return;
            }

            // 发送到API
            // SendToApiWithRole(recognizedText, roleName, null);
            // 实时识别只负责显示，不在此处上传，录音停止时再统一上传 AudioClip
        }
        catch (System.Exception e)
        {
            Debug.LogError($"处理识别结果时出错: {e.Message}");
            AddSystemMessage($"处理错误: {e.Message}", true);
        }
    }

    private void OnApiResponseTextReceived(string responseText)
    {
        Debug.Log($"收到API文本响应: {responseText}");
    }

    private void SendToApiWithRole(string text, string role, AudioClip audioClip)
    {
        //if (CozeApiManager.Instance == null)
        //{
        //    Debug.LogError("CozeApiManager 未初始化！");
        //    AddSystemMessage("API管理器未就绪", true);
        //    return;
        //}
        
        // 显示"正在思考..."提示
        lastThinkingMessage = $"{role}: 正在思考...";
        AddMessage(lastThinkingMessage, Color.gray);
        isProcessingApiResponse = true;
        StartWaitingAudio(role); // 播放等待音效

        // 发送到API，指定角色
        //CozeApiManager.Instance.SendTextToApiWithRole(text, role, audioClip, (responseText, audioUrl, hasAudio) =>
        //{
        //    StopWaitingAudio(); // 收到响应后停止等待音效
        //    // 移除"正在思考..."提示
        //    RemoveLastLineIfMatches(lastThinkingMessage);
        //    isProcessingApiResponse = false;

        //    // 检查是否有错误
        //    if (responseText.Contains("失败") || responseText.Contains("错误"))
        //    {
        //        AddSystemMessage(responseText, true);
        //        return;
        //    }

        //    // 显示AI回复
        //    string responseMsg = $"{role}: {responseText}";
        //    if (hasAudio)
        //    {
        //        responseMsg += " 🔊";
        //    }
        //    AddAiMessage(responseMsg);
        //});
    }

    // 开始录制音频（用于上传，同时可离线转写）
    private void StartAudioRecording()
    {
        if (!interactionsReady || isProcessingApiResponse) return;
        if (isRecordingAudio) return;

        Debug.Log("开始录音...");
        isRecordingAudio = true;
        AddSystemMessage("开始录音...");
        lastRecognizedText = "";

        // 实时识别（VoiceProcessor）
        DoubaoSpeechToText?.StartRecording();

        // 录制上传用的整段音频
        //currentRecording = Microphone.Start(null, false, (int)maxRecordingTime, CozeApiManager.Instance.recordingSampleRate);
        Invoke(nameof(StopAudioRecording), maxRecordingTime); // 超时自动停止
    }

    // 停止录制并上传（实时识别已结束）
    private void StopAudioRecording()
    {
        if (!isRecordingAudio) return;

        Debug.Log("停止录音...");
        isRecordingAudio = false;

        // 停止实时识别
        DoubaoSpeechToText?.StopRecording();

        // 如有超时 Invoke，可取消
        CancelInvoke(nameof(StopAudioRecording));

        // 从 VoiceProcessor 缓存拼出本次录音的 AudioClip
        //AudioClip clip = DoubaoSpeechToText?.GetCapturedClipAndClear();

        //if (clip == null)
        //{
        //    AddSystemMessage("录制失败，没有音频数据", true);
        //    return;
        //}

        //AddSystemMessage($"录制完成，音频长度: {clip.length:0.0}秒");

        //// 上传音频，附带最后一次实时识别到的文本（若为空则传空串）
        //SendToApiWithRole(lastRecognizedText ?? "", roleName, clip);

        //// 由 DoubaoSpeechToText 创建的 clip 调用方负责销毁
        //Destroy(clip);
    }

    // 发送测试音频
    public void SendTestAudio(AudioClip testClip)
    {
        if (testClip == null)
        {
            AddSystemMessage("测试音频为空", true);
            return;
        }

        AddSystemMessage($"发送测试音频，长度: {testClip.length:0.0}秒");
        SendToApiWithRole("这是一个测试音频", roleName, testClip);
    }

    private void AddUserMessage(string text)
    {
        Debug.Log($"玩家: {text}");
        AddMessage($"玩家: {text}", userTextColor);
    }

    private void AddAiMessage(string text)
    {
        AddMessage(text, aiTextColor);
    }

    public void AddSystemMessage(string text, bool isError = false)
    {
        AddMessage($"系统: {text}", isError ? errorTextColor : systemTextColor);
    }

    private void AddMessage(string text, Color color)
    {
        if (DialogText == null) return;

        string colorHex = ColorUtility.ToHtmlStringRGB(color);
        DialogText.text += $"<color=#{colorHex}>{text}</color>\n\n";

        TrimDialogLines();
    }

    // 保留最后 maxDisplayLines*2 行（含空行），清理旧文本
    private void TrimDialogLines()
    {
        if (DialogText == null) return;

        var lines = DialogText.text.Split('\n');
        int maxLinesWithSpacing = maxDisplayLines * 2; // 因为每条消息后有一个空行
        if (lines.Length > maxLinesWithSpacing)
        {
            DialogText.text = string.Join("\n", lines, lines.Length - maxLinesWithSpacing, maxLinesWithSpacing);
        }
    }

    private void RemoveLastLineIfMatches(string textToRemove)
    {
        if (DialogText == null || string.IsNullOrEmpty(DialogText.text)) return;

        if (DialogText.text.Contains(textToRemove))
        {
            var lines = DialogText.text.Split('\n');
            DialogText.text = "";

            bool found = false;
            for (int i = 0; i < lines.Length; i++)
            {
                if (!found && lines[i].Contains(textToRemove))
                {
                    found = true;
                    continue; // 跳过这一行
                }

                if (!string.IsNullOrEmpty(lines[i]))
                {
                    DialogText.text += lines[i] + "\n";
                }
            }
        }
    }

    private void ClearDialog()
    {
        if (DialogText != null)
        {
            DialogText.text = $"=== 语音对话系统 (角色: {roleName}) ===\n\n";
        }
    }

    // 公开方法，供UI按钮调用
    //public void ClearConversation()
    //{
    //    ClearDialog();
    //    CozeApiManager.Instance?.ClearConversationHistory();
    //    AddSystemMessage("对话已清空");
    //}

    // 设置角色的公共方法
    public void SetRole(string newRoleName)
    {
        if (string.IsNullOrEmpty(newRoleName))
        {
            Debug.LogWarning("角色名称不能为空");
            return;
        }

        string oldRole = roleName;
        roleName = newRoleName;

        ClearDialog();
        AddSystemMessage($"角色已从 {oldRole} 切换为 {roleName}");

        Debug.Log($"角色已切换: {oldRole} -> {roleName}");
    }

    // 切换音频上传功能
    public void ToggleAudioUpload(bool enable)
    {
        sendAudioWithText = enable;
        string status = enable ? "启用" : "禁用";
        AddSystemMessage($"音频上传功能已{status}");
    }

    // ---------- 新增：注册单按钮按下/抬起事件 ----------
    private void RegisterStartStopButtonEvents(Button targetButton)
    {
        var trigger = targetButton.GetComponent<EventTrigger>();
        if (trigger == null)
        {
            trigger = targetButton.gameObject.AddComponent<EventTrigger>();
        }

        if (trigger.triggers == null)
        {
            trigger.triggers = new List<EventTrigger.Entry>();
        }

        AddTrigger(trigger, EventTriggerType.PointerDown, _ => OnStartStopButtonDown());
        AddTrigger(trigger, EventTriggerType.PointerUp, _ => OnStartStopButtonUp());
    }

    private void AddTrigger(EventTrigger trigger, EventTriggerType type, System.Action<BaseEventData> callback)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(new UnityEngine.Events.UnityAction<BaseEventData>(callback));
        trigger.triggers.Add(entry);
    }

    private void OnStartStopButtonDown()
    {
        // 初始化阶段或处理中不响应
        if (!interactionsReady || isProcessingApiResponse) return;

        // 实时识别
        StartAudioRecording();
    }

    private void OnStartStopButtonUp()
    {
        if (!interactionsReady) return;

        // 停止录音并上传
        StopAudioRecording();
    }

    private void StartWaitingAudio(string role)
    {
        if (waitingAudioSource == null) return;
        var clip = GetWaitingClipByRole(role);
        if (clip == null) return;
        StopWaitingAudio(); // 确保先停掉上一次协程/播放
        waitingAudioRoutine = StartCoroutine(PlayWaitingOnceAfterDelay(clip, 0.5f));
    }
 
    private void StopWaitingAudio()
    {
        if (waitingAudioSource == null) return;
        if (waitingAudioRoutine != null)
        {
            StopCoroutine(waitingAudioRoutine);
            waitingAudioRoutine = null;
        }
        waitingAudioSource.Stop();
        waitingAudioSource.clip = null;
    }

    private IEnumerator PlayWaitingOnceAfterDelay(AudioClip clip, float delaySeconds)
    {
        yield return new WaitForSeconds(delaySeconds);
        waitingAudioSource.loop = false;   // 只播放一次
        waitingAudioSource.clip = clip;
        waitingAudioSource.Play();
        waitingAudioRoutine = null;        // 播放触发后清理句柄
    }

    private AudioClip GetWaitingClipByRole(string role)
    {
        if (string.IsNullOrEmpty(role)) return defaultWaitingClip;
        if (role.Contains("东")) return eastWaitingClip ?? defaultWaitingClip;
        if (role.Contains("西")) return westWaitingClip ?? defaultWaitingClip;
        if (role.Contains("南")) return southWaitingClip ?? defaultWaitingClip;
        if (role.Contains("北")) return northWaitingClip ?? defaultWaitingClip;
        return defaultWaitingClip;
    }
}